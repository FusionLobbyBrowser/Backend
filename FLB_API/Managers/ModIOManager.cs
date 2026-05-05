using System.Text.Json;

namespace FLB_API.Managers
{
    public static class ModIOManager
    {
        private const int GAME_ID = 3809;

        private readonly static List<MemoryThumbnail> Thumbnails = [];

        private static async Task<RemoteThumbnailResponse?> GetRemoteModThumbnailUrl(long modId)
        {
            Program.Logger?.Information($"Remotely fetching mod thumbnail for {modId}");

            if (string.IsNullOrWhiteSpace(Program.Settings?.ModIO_Token) || Program.Settings.ModIO_Token == "your-token")
            {
                Program.Logger?.Warning("Mod.io token is not set. Cannot fetch remote mod thumbnail.");
                return null;
            }

            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://g-{GAME_ID}.modapi.io/v1/games/{GAME_ID}/mods/{modId}");
            request.Headers.Add("Authorization", $"Bearer {Program.Settings.ModIO_Token}");
            request.Headers.Add("Accept", "application/json");
            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var json = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
            bool maturity = json.GetProperty("maturity_option").GetInt16() == 8;
            string? thumbnail = null;

            if (json.TryGetProperty("logo", out var logoElement))
                thumbnail = logoElement.GetProperty("thumb_320x180").GetString();

            if (thumbnail is null || json.GetProperty("visible").GetInt16() == 0)
                return null;
            else
                return new RemoteThumbnailResponse(modId, thumbnail, DateTimeOffset.Now.AddSeconds((long)(Program.Settings?.ThumbnailCacheExpireTime ?? 30 * 60)), maturity);
        }

        public static async Task<MemoryThumbnail?> GetModThumbnail(long modId, string barcode = "")
        {
            try
            {
                Program.Logger?.Information($"Getting mod thumbnail for {modId} ({barcode ?? "N/A"})");
                if (modId == -1)
                    return GetWithBarcode(barcode);

                var item = Thumbnails.FirstOrDefault(x => x.ModId == modId);
                if (item != null)
                {
                    if ((DateTimeOffset.Now - item.ExpireTime).TotalSeconds < (long)(Program.Settings?.ThumbnailCacheExpireTime ?? 30 * 60))
                    {
                        Program.Logger?.Information($"Found cached mod thumbnail for {modId}");
                        if (!string.IsNullOrWhiteSpace(barcode) && !item.Barcodes.Contains(barcode))
                            item.Barcodes.Add(barcode);
                        return item;
                    }
                    else
                    {
                        Program.Logger?.Information("Found an outdated thumbnail, removing...");
                        Thumbnails.Remove(item);
                    }
                }

                var remoteThumbnail = await GetRemoteModThumbnailUrl(modId);
                if (remoteThumbnail is not null)
                {
                    var image = await GetImage(remoteThumbnail.ThumbnailUrl);
                    item = new MemoryThumbnail(remoteThumbnail.ModId, image, remoteThumbnail.ExpireTime, remoteThumbnail.IsNSFW);
                    if (!string.IsNullOrWhiteSpace(barcode) && !item.Barcodes.Contains(barcode))
                        item.Barcodes.Add(barcode);
                    Thumbnails.Add(item);
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

        private static MemoryThumbnail? GetWithBarcode(string? barcode)
        {
            if (string.IsNullOrWhiteSpace(barcode))
                return null;

            Program.Logger?.Information($"A barcode was only provided, trying to find an existing cache...");
            var _item = Thumbnails.FirstOrDefault(x => x.Barcodes?.Contains(barcode) == true);
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

        private static async Task<byte[]> GetImage(string url)
        {
            using HttpClient client = new();
            using var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }
    }

    public class RemoteThumbnailResponse(long modId, string thumbnailUrl, DateTimeOffset expire, bool isNSFW = false)
    {
        public long ModId { get; set; } = modId;
        public string ThumbnailUrl { get; set; } = thumbnailUrl;

        public bool IsNSFW { get; set; } = isNSFW;

        public DateTimeOffset ExpireTime { get; set; } = expire;
    }

    public class MemoryThumbnail(long modId, byte[] image, DateTimeOffset expire, bool isNSFW = false)
    {
        public long ModId { get; set; } = modId;
        public byte[] Image { get; set; } = image;

        public bool IsNSFW { get; set; } = isNSFW;

        // Sometimes the levels do not have a mod id associated, this will be used to counter that
        public List<string> Barcodes { get; set; } = [];

        public DateTimeOffset ExpireTime { get; set; } = expire;
    }
}