using System.Runtime;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using FLB_API.Controllers.Steam;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

using SteamWebAPI2.Utilities;

namespace FLB_API.Managers
{
    public static partial class ModIOManager
    {
        private const int GAME_ID = 3809;

        internal readonly static List<MemoryThumbnail> Thumbnails = new(1000);

        public static bool IsSetup { get; private set; } = false;

        private static readonly Lock _lock = new();

        private static readonly HttpClient HttpClient = new();

        public static async Task Setup()
        {
            lock (_lock)
            {
                if (IsSetup) return;
                IsSetup = true;
            }

            while (true)
            {
                await Task.Delay((Program.Settings?.ThumbnailCleanupInterval ?? (60 * 60)) * 1000);
                int count;
                lock (_lock) count = Thumbnails.Count;
                Program.Logger?.Information($"Starting cleanup process! Processing {count} thumbnails...");
                List<MemoryThumbnail> toRemove;
                lock (_lock)
                {
                    toRemove = [.. Thumbnails.Where(x => !x.IsThumbnailValid())];
                    foreach (var r in toRemove)
                        Thumbnails.Remove(r);
                }

                Program.Logger?.Information($"Removed {toRemove.Count} thumbnails!");

                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
#pragma warning disable S1215
                // need to improve this later
                GC.Collect();
#pragma warning restore S1215
            }
        }

        private static async Task<RemoteThumbnailResponse?> GetRemoteModThumbnailUrl(long modId)
        {
            Program.Logger?.Information($"Remotely fetching mod thumbnail for {modId}");

            if (string.IsNullOrWhiteSpace(Program.Settings?.ModIO_Token) || Program.Settings.ModIO_Token == "your-token")
            {
                Program.Logger?.Warning("Mod.io token is not set. Cannot fetch remote mod thumbnail.");
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://g-{GAME_ID}.modapi.io/v1/games/{GAME_ID}/mods/{modId}");
            request.Headers.Add("Authorization", $"Bearer {Program.Settings.ModIO_Token}");
            request.Headers.Add("Accept", "application/json");
            using var response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var json = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
            bool maturity = json.GetProperty("maturity_option").GetInt16() == 8;
            string? thumbnail = null;

            if (json.TryGetProperty("logo", out var logoElement))
                thumbnail = logoElement.GetProperty("thumb_320x180").GetString();

            if (thumbnail is null)
                return null;
            else
                return new RemoteThumbnailResponse(modId, thumbnail, DateTimeOffset.Now.AddSeconds((long)(Program.Settings?.ThumbnailCacheExpireTime ?? (30 * 60))), maturity);
        }

        public static async Task<MemoryThumbnail?> GetModThumbnail(long modId, string barcode = "")
        {
            try
            {
                Program.Logger?.Information($"Getting mod thumbnail for {modId} ({barcode ?? "N/A"})");
                if (modId == -1)
                    return GetWithBarcode(barcode);

                MemoryThumbnail? item;
                lock (_lock)
                    item = Thumbnails.FirstOrDefault(x => x.ModId == modId);
                if (item != null)
                {
                    if (item.IsThumbnailValid())
                    {
                        Program.Logger?.Information($"Found cached mod thumbnail for {modId}");
                        lock (_lock)
                        {
                            if (!string.IsNullOrWhiteSpace(barcode) && !item.Barcodes.Contains(barcode))
                                item.Barcodes.Add(barcode);
                        }
                        return item;
                    }
                    else
                    {
                        Program.Logger?.Information("Found an outdated thumbnail, removing...");
                        lock (_lock)
                            Thumbnails.Remove(item);
                    }
                }

                var remoteThumbnail = await GetRemoteModThumbnailUrl(modId);
                if (remoteThumbnail is not null)
                {
                    var image = await GetImage(remoteThumbnail.ThumbnailUrl);
                    item = new MemoryThumbnail(remoteThumbnail.ModId, image, remoteThumbnail.ExpireTime, remoteThumbnail.IsNSFW);
                    lock (_lock)
                    {
                        if (!string.IsNullOrWhiteSpace(barcode) && !item.Barcodes.Contains(barcode))
                            item.Barcodes.Add(barcode);

                        Thumbnails.Add(item);
                    }
                    return item;
                }
                return null;
            }
            catch (Exception ex)
            {
                Program.Logger?.Error(ex, $"Error getting mod thumbnail for {modId}");
                return null;
            }
        }

        public static async Task<bool> IsNSFW(long modId, string barcode = "")
        {
            try
            {
                if (modId == -1)
                    return GetWithBarcode(barcode)?.IsNSFW ?? false;

                MemoryThumbnail? item;
                lock (_lock)
                    item = Thumbnails.FirstOrDefault(x => x.ModId == modId);
                if (item != null)
                    return item.IsNSFW;
                else
                    return (await GetRemoteModThumbnailUrl(modId))?.IsNSFW ?? false;
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
                Program.Logger?.Information("An invalid barcode was provided! Barcode: " + barcode);
                return null;
            }

            Program.Logger?.Information("A barcode was only provided, trying to find an existing cache...");
            MemoryThumbnail? _item;
            lock (_lock)
                _item = Thumbnails.FirstOrDefault(x => x.Barcodes?.Contains(barcode) == true);
            // This ignores cache, as level without mod id is quite rare and theres a chance there will be another request to have a mod id associated
            if (_item != null)
            {
                Program.Logger?.Information($"Found cached mod thumbnail for {barcode}");
                return _item;
            }
            else
            {
                Program.Logger?.Information($"Could not find a cached thumbnail for {barcode}");
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

    public class RemoteThumbnailResponse(long modId, string thumbnailUrl, DateTimeOffset? expire, bool isNSFW = false)
    {
        public long ModId { get; set; } = modId;
        public string ThumbnailUrl { get; set; } = thumbnailUrl;

        public bool IsNSFW { get; set; } = isNSFW;

        public DateTimeOffset? ExpireTime { get; set; } = expire;
    }

    public sealed class MemoryThumbnail(long modId, byte[] image, DateTimeOffset? expire, bool isNSFW = false)
    {
        public long ModId { get; set; } = modId;
        public byte[] Image { get; set; } = image;

        public bool IsNSFW { get; set; } = isNSFW;

        // Sometimes the levels do not have a mod id associated, this will be used to counter that
        public List<string> Barcodes { get; set; } = [];

        public DateTimeOffset? ExpireTime { get; set; } = expire;
    }
}