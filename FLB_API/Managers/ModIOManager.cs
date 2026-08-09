using System.Runtime;
using System.Text.Json;
using System.Text.RegularExpressions;

using FusionAPI.Data.Containers;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace FLB_API.Managers
{
    public static partial class ModIOManager
    {
        private const int GameId = 3809;

        private const int CollectionId = 92630;

        internal static readonly List<MemoryThumbnail> Thumbnails = new(1000);

        public static bool IsSetup { get; private set; } = false;

        private static readonly Lock Lock = new();

        private static readonly HttpClient HttpClient = new();

        private static List<int> _furryMods = [];

        public static IReadOnlyList<int> FurryMods => _furryMods.AsReadOnly();

        public static async Task Setup()
        {
            lock (Lock)
            {
                if (IsSetup) return;
                IsSetup = true;
            }

            try
            {
                _ = FetchAvatarsFromCollection();
            }
            catch (Exception ex)
            {
                Program.Logger?.Error(ex, "An unexpected error has occurred while fetching avatars from collection");
            }
            while (true)
            {
                await Task.Delay((Program.Settings?.ThumbnailCleanupInterval ?? (60 * 60)) * 1000);
                int count;
                lock (Lock) count = Thumbnails.Count;
                Program.Logger?.Information("Starting cleanup process! Processing {0} thumbnails...", count);
                List<MemoryThumbnail> toRemove;
                lock (Lock)
                {
                    toRemove = [.. Thumbnails.Where(x => !x.IsThumbnailValid())];
                    foreach (var r in toRemove)
                    {
                        r.Image = null;
                        r.Barcodes = null;
                        r.ExpireTime = null;
                        r.ModId = -1;
                        Thumbnails.Remove(r);
                    }
                }

                Program.Logger?.Information("Removed {0} thumbnails!", toRemove.Count);

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
                    Program.Logger?.Error(ex, "An unexpected error has occurred while fetching avatars from collection");
                }
            }
        }

        private static async Task<RemoteThumbnailResponse?> GetRemoteModThumbnailUrl(long modId)
        {
            Program.Logger?.Information("Remotely fetching mod thumbnail for {0}", modId);

            if (string.IsNullOrWhiteSpace(Program.Settings?.ModIoToken) || Program.Settings.ModIoToken == "your-token")
            {
                Program.Logger?.Warning("Mod.io token is not set. Cannot fetch remote mod thumbnail.");
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://g-{GameId}.modapi.io/v1/games/{GameId}/mods/{modId}");
            request.Headers.Add("Authorization", $"Bearer {Program.Settings.ModIoToken}");
            request.Headers.Add("Accept", "application/json");
            using var response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var json = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
            var maturity = json.GetProperty("maturity_option").GetInt16() == 8;
            string? thumbnail = null;

            if (json.TryGetProperty("logo", out var logoElement))
                thumbnail = logoElement.GetProperty("thumb_320x180").GetString();

            return thumbnail is null ? null : new RemoteThumbnailResponse(modId, thumbnail, DateTimeOffset.Now.AddSeconds((long)(Program.Settings?.ThumbnailCacheExpireTime ?? 30 * 60)), maturity);
        }

        public static async Task FetchAvatarsFromCollection()
        {
            Program.Logger?.Information("Getting avatars from collection {0}", CollectionId);

            if (string.IsNullOrWhiteSpace(Program.Settings?.ModIoToken) || Program.Settings.ModIoToken == "your-token")
            {
                Program.Logger?.Warning("Mod.io token is not set. Cannot fetch mods.");
                return;
            }

            var offset = 0;
            var total = -1;
            List<int> mods = [];

            do
            {
                Program.Logger?.Information("Fetching avatars... ({0} offset out of {1})", offset, total == -1 ? "unknown" : total);
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://g-{GameId}.modapi.io/v1/games/{GameId}/collections/{CollectionId}/mods?_offset={offset}");
                request.Headers.Add("Authorization", $"Bearer {Program.Settings.ModIoToken}");
                request.Headers.Add("Accept", "application/json");
                using var response = await HttpClient.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    Program.Logger?.Error("Failed to fetch avatars from collection. Status code: {0} ({1})\n{2}", response.StatusCode, response.ReasonPhrase, body);
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
            Program.Logger?.Information("Updated list, now has {0} furry mods", mods.Count);
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
                Program.Logger?.Information("Getting mod thumbnail for {0} ({1})", modId, barcode ?? "N/A");
                if (modId == -1)
                    return GetWithBarcode(barcode);

                MemoryThumbnail? item;
                lock (Lock)
                    item = Thumbnails.FirstOrDefault(x => x.ModId == modId);
                if (item != null)
                {
                    if (item.IsThumbnailValid())
                    {
                        Program.Logger?.Information("Found cached mod thumbnail for {0}", modId);
                        lock (Lock)
                        {
                            if (!string.IsNullOrWhiteSpace(barcode) && !item.Barcodes.Contains(barcode))
                                item.Barcodes.Add(barcode);
                        }
                        return item;
                    }
                    else
                    {
                        Program.Logger?.Information("Found an outdated thumbnail, removing...");
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
                Program.Logger?.Error(ex, "Error getting mod thumbnail for {0}", modId);
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
                Program.Logger?.Error(ex, "An unexpected error has occurred while checking if a mod is NSFW");
                return false;
            }
        }

        private static bool IsThumbnailValid(this MemoryThumbnail item)
            => item.ExpireTime == null || (DateTimeOffset.Now - item.ExpireTime.Value).TotalSeconds < (long)(Program.Settings?.ThumbnailCacheExpireTime ?? (30 * 60));

        private static MemoryThumbnail? GetWithBarcode(string? barcode)
        {
            if (string.IsNullOrWhiteSpace(barcode))
                return null;

            if (!IsValidBarcode(barcode))
            {
                Program.Logger?.Information("An invalid barcode was provided! Barcode: {0}", barcode);
                return null;
            }

            Program.Logger?.Information("A barcode was only provided, trying to find an existing cache...");
            MemoryThumbnail? item;
            lock (Lock)
                item = Thumbnails.FirstOrDefault(x => x.Barcodes?.Contains(barcode) == true);
            // This ignores cache, as level without mod id is quite rare and there's a chance there will be another request to have a mod id associated
            if (item != null)
            {
                Program.Logger?.Information("Found cached mod thumbnail for {0}", barcode);
                return item;
            }
            else
            {
                Program.Logger?.Information("Could not find a cached thumbnail for {0}", barcode);
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

        [GeneratedRegex(@"^[a-zA-Z]{1,}?\.[a-zA-Z]{1,}?\.[a-zA-Z]{1,}?\.[a-zA-Z]{1,}?$")]
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
        public List<string>? Barcodes { get; set; } = [];

        public DateTimeOffset? ExpireTime { get; set; } = expire;

        ~MemoryThumbnail()
        {
            Image = null;
            Barcodes = null;
            ExpireTime = null;
            ModId = -1;
        }
    }
}