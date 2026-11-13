using System.Buffers;
using System.Net;
using System.Net.WebSockets;
using System.Runtime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using FusionAPI.Data.Containers;

using NetCord.Services;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace FLB_API.Managers
{
    public static partial class ThumbnailManager
    {
        internal const int GameId = 3809;

        private const int CollectionId = 92630;

        internal static readonly List<MemoryThumbnail> Thumbnails = [with(1000)];

        public static bool IsSetup { get; private set; }

        private static readonly Lock Lock = new();

        private static readonly HttpClient HttpClient = new();

        private static List<int> _furryMods = [];

        public static IReadOnlyList<int> FurryMods => _furryMods.AsReadOnly();

        public static FusionAPI.Data.Interfaces.ILogger? Logger { get; set; }

        private static bool IsAuthenticating { get; set; } = false;

        private static DeviceLoginInitialResponseContext AuthCode { get; set; }

        public static async Task Setup()
        {
            lock (Lock)
            {
                if (IsSetup) return;
                IsSetup = true;
            }

            Logger?.Info("Ensuring token is valid...");
            if (Program.Settings?.ModIo?.Token?.IsValid() == true)
            {
                Logger?.Info("Token is provided, making sample request to API");
                // I hope this mod does NOT get randomly nuked from the face of mod.io, i believe in you
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    $"https://g-{GameId}.modapi.io/v1/games/{GameId}/mods/4067166");
                request.Headers.Add("Authorization", $"Bearer {Program.Settings?.ModIo?.Token}");
                request.Headers.Add("Accept", "application/json");
                using var response = await HttpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    Logger?.Error("Token is not valid, re-authenticating...");
                    await Authenticate();
                }
                else
                {
                    Logger?.Info("Token is valid!");
                }
            }
            else
            {
                Logger?.Error("Token is not valid, re-authenticating...");
                await Authenticate();
            }

            try
            {
                _ = FetchAvatarsFromCollection();
            }
            catch (Exception ex)
            {
                Logger?.Error("An unexpected error has occurred while fetching avatars from collection", ex);
            }

            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay((Program.Settings?.ModIo?.CleanupInterval ?? (60 * 60)) * 1000);
                    int count;
                    lock (Lock) count = Thumbnails.Count;
                    Logger?.Info("Starting cleanup process! Processing {0} thumbnails...", count);
                    List<MemoryThumbnail> toRemove;
                    lock (Lock)
                    {
                        toRemove = [.. Thumbnails.Where(x => !x.IsThumbnailValid())];
                        foreach (var r in toRemove)
                        {
                            r.Image = null;
                            r.Barcodes = [];
                            r.ExpireTime = null;
                            r.ModId = -1;
                            Thumbnails.Remove(r);
                        }
                    }

                    Logger?.Info("Removed {0} thumbnails!", toRemove.Count);

                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
#pragma warning disable S1215
                    // need to improve this later
                    GC.Collect();
#pragma warning restore S1215
                    try
                    {
                        await FetchAvatarsFromCollection();
                    }
                    catch (Exception ex)
                    {
                        Logger?.Error("An unexpected error has occurred while fetching avatars from collection", ex);
                    }
                }
            });
        }

        private static async Task Authenticate()
        {
            if (IsAuthenticating)
            {
                if (Program.Settings?.ModIo?.AccountToken?.IsValid() == true || Program.Settings?.ModIo?.Auth?.Use == true) return;
                Logger?.Info("No account token provided, please log in using the following URL: {0}", AuthCode.LoginUrl ?? "N/A");
                Logger?.Info("You have until {0} to log in!", DateTimeOffset.FromUnixTimeSeconds(AuthCode.DateExpires));

                return;
            }

            try
            {
                IsAuthenticating = true;
                var uri = $"wss://g-{GameId}.ws.modapi.io/";
                using ClientWebSocket ws = new();
                Logger?.Info("Initializing WebSocket connection to {0}", uri);
                await ws.ConnectAsync(new Uri(uri), CancellationToken.None);
                Logger?.Info("Sending initial device login request");
                await ws.SendAsync(JsonSerializer.SerializeToUtf8Bytes(WebSocketMessages.LogIn()),
                    WebSocketMessageType.Text, true, default);
                Logger?.Info("Awaiting response to get code...");
                var bytes = ArrayPool<byte>.Shared.Rent(4096);
                var result = await ws.ReceiveAsync(bytes, CancellationToken.None);
                var res = Encoding.UTF8.GetString(bytes, 0, result.Count);
                var initialMessage = JsonSerializer.Deserialize<WebSocketMessages>(res);
                var first = initialMessage?.Messages?.FirstOrDefault();
                if (first?.Operation == "device_login" &&
                    first?.Context.TryGetProperty("login_url", out _) == true)
                {
                    var context = first.Context.Deserialize<DeviceLoginInitialResponseContext>();
                    AuthCode = context;
                    if (string.IsNullOrWhiteSpace(context.Code))
                    {
                        Logger?.Error("Provided code from the WebSocket is empty.");
                        return;
                    }
                    Logger?.Info("Logging in using code {0}", context.Code);

                    var dontSendReq = false;

                    if (Program.Settings?.ModIo?.AccountToken?.IsValid() != true)
                    {
                        var manual = true;
                        if (Program.Settings?.ModIo?.Auth?.Use == true)
                        {
                            Logger?.Info("Account token not provided or has expired, attempting to log in");
                            var success = await LogIn();
                            if (!success)
                            {
                                Logger?.Error(
                                    "Failed to log in with the provided credentials. Manual login required.");
                            }
                            else
                            {
                                manual = false;
                            }
                        }

                        if (manual)
                        {
                            dontSendReq = true;
                            Logger?.Info(
                                "No account token provided or has expired, please log in using the following URL: {0}",
                                context.LoginUrl ?? "N/A");
                            Logger?.Info("You have until {0} to log in!",
                                DateTimeOffset.FromUnixTimeSeconds(context.DateExpires));
                        }
                    }

                    if (!dontSendReq && !await SendAuthReq(context.Code))
                    {
                        Logger?.Error(
                            "Failed to authenticate with the provided code. Ensure that the account token is correct!");
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                        return;
                    }

                    var aBytes = ArrayPool<byte>.Shared.Rent(4096);
                    var aResult = await ws.ReceiveAsync(aBytes, CancellationToken.None);
                    var ares = Encoding.UTF8.GetString(aBytes, 0, aResult.Count);
                    var aMessage = JsonSerializer.Deserialize<WebSocketMessages>(ares);
                    var authFirst = aMessage?.Messages?.FirstOrDefault();

                    if (authFirst is not { Operation: "login_success" } ||
                        authFirst?.Context.TryGetProperty("access_token", out _) != true)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                    }

                    var finalContext = authFirst!.Context.Deserialize<DeviceLoginFinalResponseContext>();
                    Logger?.Info("Successfully authenticated! Token expires at {0}",
                        DateTimeOffset.FromUnixTimeSeconds(finalContext.DateExpires));
                    Program.Settings?.ModIo?.Token = new Token(finalContext.AccessToken!, finalContext.DateExpires);
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                }
            }
            finally
            {
                IsAuthenticating = false;
            }
        }

        private static async Task<bool> LogIn()
        {
            var email = Program.Settings?.ModIo?.Auth?.Email;
            if (string.IsNullOrWhiteSpace(email))
            {
                Logger?.Warning("No email provided for Mod.io login.");
                return false;
            }

            if (Program.ImapEmpty())
            {
                Logger?.Warning("IMAP is not configured for Mod.io login.");
                return false;
            }

            Logger?.Info("Sending request for OTP");
            using var request =
                new HttpRequestMessage(HttpMethod.Post, "https://auth.mod.io/auth/emailrequest");
            request.Headers.Add("Accept", "application/json");
            var body = new List<KeyValuePair<string, string>> { new("email", email) };
            request.Content = new FormUrlEncodedContent(body);
            using var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    Logger?.Warning("Too many requests to the email endpoint. Retrying in 5 minutes...");
                    await Task.Delay(5 * 60 * 1000);
                    return await LogIn();
                }
                Logger?.Error("Failed to request email authentication ({0})", response.StatusCode);
                Logger?.Error("Body: {0}", await response.Content.ReadAsStringAsync());
                return false;
            }

            Logger?.Info("OTP sent successfully, waiting for code...");
            var imap = new IMAPManager(Program.Settings!.Imap!, Logger, true);
            await Task.Delay((int)3.5f * 1000);
            var code = await imap.GetCodeAsync(IMAPManager.GetModIoRegEx(), "Mod.io Auth Code", IMAPManager.MODIO_SUBJECT);

            if (string.IsNullOrWhiteSpace(code))
            {
                Logger?.Error("Failed to fetch authentication code from email");
                return false;
            }

            Logger?.Info("Successfully fetched code: {0}", code);
            Logger?.Info("Logging in to retrieve account token");

            using var exchangeRequest =
                new HttpRequestMessage(HttpMethod.Post, "https://auth.mod.io/auth/emailexchange");
            exchangeRequest.Headers.Add("Accept", "application/json");
            var exchangeBody = new List<KeyValuePair<string, string>> { new("code", code) };
            exchangeRequest.Content = new FormUrlEncodedContent(exchangeBody);
            using var exchangeResponse = await HttpClient.SendAsync(request);

            if (!exchangeResponse.IsSuccessStatusCode)
            {
                Logger?.Error("Failed to log in ({0})", response.StatusCode);
                Logger?.Error("Body: {0}", await response.Content.ReadAsStringAsync());
                return false;
            }

            var json = JsonSerializer.Deserialize<JsonDocument>(await exchangeResponse.Content.ReadAsStringAsync());
            if (json == null)
            {
                Logger?.Error("Could not deserialize the JSON response");
                return false;
            }
            Logger?.Info("Successfully logged in as {0}!", json.RootElement.GetProperty("user").GetProperty("info").GetProperty("display_name").GetString() ?? "N/A");
            Logger?.Info("Token is valid until {0}", DateTimeOffset.FromUnixTimeSeconds(json.RootElement.GetProperty("date_expires").GetInt64()));

            Program.Settings?.ModIo?.AccountToken = new Token(json.RootElement.GetProperty("access_token").GetString() ?? string.Empty, json.RootElement.GetProperty("date_expires").GetInt64());

            return true;
        }

        private static async Task<bool> SendAuthReq(string code)
        {
            using var request =
                new HttpRequestMessage(HttpMethod.Post, "https://auth.mod.io/auth/device/connect");
            request.Headers.Add("Authorization", $"Bearer {Program.Settings?.ModIo?.AccountToken}");
            request.Headers.Add("Accept", "application/json");
            var body = new List<KeyValuePair<string, string>> { new("code", code) };
            request.Content = new FormUrlEncodedContent(body);
            using var response = await HttpClient.SendAsync(request);
            Logger?.Info("Server responded with {0}", response.StatusCode, response.ReasonPhrase ?? "N/A");
            return response.IsSuccessStatusCode;
        }

        private static async Task<RemoteThumbnailResponse?> GetRemoteModThumbnailUrl(long modId)
        {
            Logger?.Info("Remotely fetching mod thumbnail for {0}", modId);

            if (Program.Settings?.ModIo?.Token?.IsValid() != true)
            {
                Logger?.Warning("Mod.io token is invalid or has expired. Cannot fetch remote mod thumbnail. Attempting to re-authenticate...");
                _ = Task.Run(Authenticate);
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://g-{GameId}.modapi.io/v1/games/{GameId}/mods/{modId}");
            request.Headers.Add("Authorization", $"Bearer {Program.Settings.ModIo.Token}");
            request.Headers.Add("Accept", "application/json");
            using var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Logger?.Error($"Failed to fetch remote mod thumbnail. {CreateErrMsg(await response.Content.ReadAsStringAsync(), (int)response.StatusCode)}");
                if (response.StatusCode == HttpStatusCode.Unauthorized) _ = Task.Run(Authenticate);
                return null;
            }
            var json = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
            var maturity = json.GetProperty("maturity_option").GetInt16() == 8;
            string? thumbnail = null;

            if (json.TryGetProperty("logo", out var logoElement))
                thumbnail = logoElement.GetProperty("thumb_320x180").GetString();

            return thumbnail is null ? null : new RemoteThumbnailResponse(modId, thumbnail, DateTimeOffset.Now.AddSeconds((long)(Program.Settings?.ModIo?.CacheExpireTime ?? 30 * 60)), maturity);
        }

        public static async Task FetchAvatarsFromCollection()
        {
            Logger?.Info("Getting avatars from collection {0}", CollectionId);

            if (string.IsNullOrWhiteSpace(Program.Settings?.ModIo?.PersonalAccessToken))
            {
                Logger?.Warning("Mod.io personal access token is not set. Cannot fetch mods.");
                return;
            }

            var offset = 0;
            var total = -1;
            List<int> mods = [];

            do
            {
                Logger?.Info("Fetching avatars... ({0} offset out of {1})", offset, total == -1 ? "unknown" : total);
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://g-{GameId}.modapi.io/v1/games/{GameId}/collections/{CollectionId}/mods?_offset={offset}");
                request.Headers.Add("Authorization", $"Bearer {Program.Settings.ModIo.PersonalAccessToken}");
                request.Headers.Add("Accept", "application/json");
                using var response = await HttpClient.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    Logger?.Error($"Failed to fetch avatars from collection. {CreateErrMsg(body, (int)response.StatusCode)}");
                    return;
                }
                var json = JsonSerializer.Deserialize<JsonDocument>(body);
                json?.RootElement.GetProperty("data").EnumerateArray().ToList().ForEach(x =>
                {
                    var id = x.GetProperty("id").GetInt32();
                    var isAvatar = x.GetProperty("tags").EnumerateArray().Any(t => t.GetProperty("name").GetString() == "Avatar");
                    if (isAvatar && id != 3862839 && id != 5782395 && !mods.Contains(id))
                        mods.Add(id);
                });
                total = json?.RootElement.GetProperty("result_total").GetInt32() ?? -1;
                offset += 50;
            }
            while (total > offset);
            _furryMods = mods;
            Logger?.Info("Updated list, now has {0} furry mods", mods.Count);
        }

        public static CustomLobbyInfo Convert(this LobbyInfo info)
        {
            var hasFurries = info.PlayerList?.Players?.Any(p => FurryMods.Contains(p.AvatarModID)) ?? false;
            return new CustomLobbyInfo(info, hasFurries);
        }

        public static async Task<MemoryThumbnail?> GetModThumbnail(long modId, string? barcode = "")
        {
            try
            {
                Logger?.Info("Getting mod thumbnail for {0} ({1})", modId, barcode ?? "N/A");
                if (modId == -1)
                    return GetWithBarcode(barcode);

                MemoryThumbnail? item;
                lock (Lock)
                    item = Thumbnails.FirstOrDefault(x => x.ModId == modId);
                if (item != null)
                {
                    if (item.IsThumbnailValid())
                    {
                        Logger?.Info("Found cached mod thumbnail for {0}", modId);
                        lock (Lock)
                        {
                            if (!string.IsNullOrWhiteSpace(barcode) && !item.Barcodes.Contains(barcode))
                                item.Barcodes.Add(barcode);
                        }
                        return item;
                    }
                    else
                    {
                        Logger?.Info("Found an outdated thumbnail, removing...");
                        lock (Lock)
                            Thumbnails.Remove(item);
                    }
                }

                var remoteThumbnail = await GetRemoteModThumbnailUrl(modId);
                if (remoteThumbnail is null)
                    return null;
                var image = await GetImage(remoteThumbnail.ThumbnailUrl);
                item = new MemoryThumbnail(remoteThumbnail.ModId, image, remoteThumbnail.ExpireTime, remoteThumbnail.IsNsfw);
                lock (Lock)
                {
                    if (!string.IsNullOrWhiteSpace(barcode) && !item.Barcodes.Contains(barcode))
                        item.Barcodes.Add(barcode);

                    Thumbnails.Add(item);
                }
                return item;
            }
            catch (Exception ex)
            {
                Logger?.Error("Error getting mod thumbnail for {0}", ex, modId);
                return null;
            }
        }

        public static async Task<bool> IsNsfw(long modId, string barcode = "")
        {
            try
            {
                if (modId == -1)
                    return GetWithBarcode(barcode)?.IsNsfw ?? false;

                MemoryThumbnail? item;
                lock (Lock)
                    item = Thumbnails.FirstOrDefault(x => x.ModId == modId);
                if (item != null)
                    return item.IsNsfw;
                else
                    return (await GetRemoteModThumbnailUrl(modId))?.IsNsfw ?? false;
            }
            catch (Exception ex)
            {
                Logger?.Error("An unexpected error has occurred while checking if a mod is NSFW", ex);
                return false;
            }
        }

        private static bool IsThumbnailValid(this MemoryThumbnail item)
            => item.ExpireTime == null || (DateTimeOffset.Now - item.ExpireTime.Value).TotalSeconds < (long)(Program.Settings?.ModIo?.CacheExpireTime ?? (30 * 60));

        private static MemoryThumbnail? GetWithBarcode(string? barcode)
        {
            if (string.IsNullOrWhiteSpace(barcode))
                return null;

            if (!IsValidBarcode(barcode))
            {
                Logger?.Info("An invalid barcode was provided! Barcode: {0}", barcode);
                return null;
            }

            Logger?.Info("A barcode was only provided, trying to find an existing cache...");
            MemoryThumbnail? item;
            lock (Lock)
                item = Thumbnails.FirstOrDefault(x => x.Barcodes?.Contains(barcode) == true);
            // This ignores cache, as level without mod id is quite rare and there's a chance there will be another request to have a mod id associated
            if (item != null)
            {
                Logger?.Info("Found cached mod thumbnail for {0}", barcode);
                return item;
            }
            else
            {
                Logger?.Info("Could not find a cached thumbnail for {0}", barcode);
                return null;
            }
        }

        private static bool IsValidBarcode(string barcode)
        {
            var regex = BarcodeValidationRegex();
            return regex.IsMatch(barcode);
        }

        private static async Task<byte[]> GetImage(string url)
        {
            using var response = await HttpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            await using var bytes = await response.Content.ReadAsStreamAsync();
            const long min = (1 * 1000 * 500);
            if ((response.Content.Headers.ContentLength > min) || bytes.Length > min)
            {
                bytes.Position = 0;
                using var img = await Image.LoadAsync(bytes);
                if (img.Width > 320 || img.Height > 180)
                {
                    img.Mutate(x =>
                        x.Resize(new ResizeOptions()
                        {
                            Size = new Size(320, 180),
                            Mode = ResizeMode.Max
                        })
                    );
                    await using var stream = new MemoryStream();
                    await img.SaveAsPngAsync(stream);
                    stream.Position = 0;
                    return stream.ToArray();
                }
                else
                {
                    bytes.Position = 0;
                    await using var stream = new MemoryStream();
                    await bytes.CopyToAsync(stream);
                    stream.Position = 0;
                    return stream.ToArray();
                }
            }
            else
            {
                await using var stream = new MemoryStream();
                await bytes.CopyToAsync(stream);
                stream.Position = 0;
                return stream.ToArray();
            }
        }

        public static string CreateErrMsg(string body, int statusCode)
        {
            try
            {
                var json = JsonSerializer.Deserialize<ThumbnailRequestError>(body);
                return
                    $"Request returned code {json?.Error?.Code ?? -1} with error reference {json?.Error?.ErrorRef ?? -1} (Status Code: {statusCode}): {json?.Error?.Message ?? "Unknown error"}";
            }
            catch (JsonException)
            {
                return $"Could not deserialize error body (Status Code: {statusCode}), body: {body}";
            }
            catch (Exception ex)
            {
                Logger?.Error("An unexpected error has occurred while creating an error message", ex);
                return $"Request returned status code {statusCode} with body: {body}";
            }
        }

        [GeneratedRegex(@"^[a-zA-Z0-9]{1,}?\.[a-zA-Z0-9]{1,}?\.[a-zA-Z0-9]{1,}?\.[a-zA-Z0-9]{1,}?$")]
        private static partial Regex BarcodeValidationRegex();
    }

    public class RemoteThumbnailResponse(long modId, string thumbnailUrl, DateTimeOffset? expire, bool isNsfw = false)
    {
        public long ModId { get; set; } = modId;
        public string ThumbnailUrl { get; set; } = thumbnailUrl;

        public bool IsNsfw { get; set; } = isNsfw;

        public DateTimeOffset? ExpireTime { get; set; } = expire;
    }

    public sealed class MemoryThumbnail(long modId, byte[] image, DateTimeOffset? expire, bool isNsfw = false)
    {
        public long ModId { get; set; } = modId;
        public byte[]? Image { get; set; } = image;

        public bool IsNsfw { get; set; } = isNsfw;

        // Sometimes the levels do not have a mod id associated, this will be used to counter that
        public List<string> Barcodes { get; set; } = [];

        public DateTimeOffset? ExpireTime { get; set; } = expire;

        ~MemoryThumbnail()
        {
            Image = null;
            Barcodes = [];
            ExpireTime = null;
            ModId = -1;
        }
    }

    [method: JsonConstructor]
    public class ThumbnailRequestError()
    {
        [JsonPropertyName("error")]
        public ThumbnailRequestErrorDetails? Error { get; set; }
    }

    [method: JsonConstructor]
    public class ThumbnailRequestErrorDetails()
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("error_ref")]
        public int ErrorRef { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("errors")]
        public JsonDocument? Errors { get; set; }
    }

    [method: JsonConstructor]
    public class WebSocketMessages()
    {
        [JsonPropertyName("messages")]
        public List<GenericWebSocketMessage> Messages { get; set; } = [];

        public static WebSocketMessages LogIn()
        {
            var msg = new WebSocketMessages();
            msg.Messages.Add(new GenericWebSocketMessage("device_login", new DeviceLoginRequestContext(ThumbnailManager.GameId)));
            return msg;
        }
    }

    public class GenericWebSocketMessage
    {
        [JsonPropertyName("operation")]
        public string? Operation { get; set; }

        [JsonPropertyName("context")]
        public JsonElement Context { get; set; }

        public GenericWebSocketMessage(string operation, JsonElement context)
        {
            Operation = operation;
            Context = context;
        }

        public GenericWebSocketMessage(string operation, object context)
        {
            Operation = operation;
            Context = JsonElement.Parse(JsonSerializer.Serialize(context));
        }

        [JsonConstructor]
        public GenericWebSocketMessage()
        {
        }
    }

    public struct DeviceLoginRequestContext
    {
        [JsonPropertyName("game_id")]
        public int GameId { get; set; }

        public DeviceLoginRequestContext(int gameId)
        {
            GameId = gameId;
        }

        [JsonConstructor]
        public DeviceLoginRequestContext()
        {
        }
    }

    [method: JsonConstructor]
    public struct DeviceLoginFinalResponseContext()
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("date_expires")]
        public long DateExpires { get; set; }
    }

    [method: JsonConstructor]
    public struct DeviceLoginInitialResponseContext()
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("login_url")]
        public string? LoginUrl { get; set; }

        [JsonPropertyName("date_expires")]
        public long DateExpires { get; set; }
    }
}