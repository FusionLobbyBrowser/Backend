using System.Text.Json;

namespace FLB_API.Managers
{
    public static class ModIOManager
    {
        private const string FileFormat = "{mod_id}-{expire_time}-{maturity}.png";

        private const int GAME_ID = 3809;

        // The images are relatively small so it SHOULD work, hopefully
        private const bool STORE_IN_MEMORY = true;

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

        public static async Task<MemoryThumbnail?> GetModThumbnail(long modId)
        {
            try
            {
                Program.Logger?.Information($"Getting mod thumbnail for {modId}");
                if (!STORE_IN_MEMORY)
                {
                    DirectoryInfo current = new(Directory.GetCurrentDirectory());
                    DirectoryInfo cacheDir = new(Path.Combine(current.FullName, "Cache"));

                    if (!cacheDir.Exists)
                        cacheDir.Create();

                    var files = cacheDir.GetFiles($"{modId}-*.png");
                    if (files.Length == 0)
                        return await CacheRemote(modId);
                    else
                        Program.Logger?.Information($"Found cached mod thumbnail for {modId}");

                    if (files.Length > 1)
                    {
                        Program.Logger?.Information($"Found duplicate cached thumbnails (Count: {files.Length}) for {modId}, removing...");
                        var sortedFiles = files.AsEnumerable().OrderByDescending(x => GetExpireFromName(Path.GetFileNameWithoutExtension(x.FullName)));
                        foreach (var _file in sortedFiles.Skip(1).Select(x => x.FullName))
                        {
                            try
                            {
                                File.Delete(_file);
                                Program.Logger?.Information($"Deleted duplicate cached thumbnail: {_file}");
                            }
                            catch (Exception ex)
                            {
                                Program.Logger?.Error(ex, $"Error deleting duplicate cached thumbnail: {_file}");
                            }
                        }
                    }

                    var file = files.FirstOrDefault();
                    var info = await GetInfo(modId, file?.Name);
                    return info ?? await CacheRemote(modId);
                }
                else
                {
                    var item = Thumbnails.FirstOrDefault(x => x.ModId == modId);
                    if (item != null)
                    {
                        if ((DateTimeOffset.Now - item.ExpireTime).TotalSeconds < (long)(Program.Settings?.ThumbnailCacheExpireTime ?? 30 * 60))
                        {
                            Program.Logger?.Information($"Found cached mod thumbnail for {modId}");
                            return item;
                        }
                        else
                        {
                            Program.Logger?.Information($"Found an outdated thumbnail, removing...");
                            Thumbnails.Remove(item);
                        }
                    }

                    var remoteThumbnail = await GetRemoteModThumbnailUrl(modId);
                    if (remoteThumbnail is not null)
                    {
                        var image = await GetImage(remoteThumbnail.ThumbnailUrl);
                        item = new MemoryThumbnail(remoteThumbnail.ModId, image, remoteThumbnail.ExpireTime, remoteThumbnail.IsNSFW);
                        Thumbnails.Add(item);
                        return item;
                    }
                    return null;
                }
            }
            catch (Exception ex)
            {
                Program.Logger?.Error(ex, $"Error getting mod thumbnail for {modId}");
                return null;
            }
        }

        private static async Task<MemoryThumbnail?> GetInfo(long modId, string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            DirectoryInfo current = new(Directory.GetCurrentDirectory());
            DirectoryInfo cacheDir = new(Path.Combine(current.FullName, "Cache"));

            if (!cacheDir.Exists)
                cacheDir.Create();

            var parts = name.Split('-');
            if (parts.Length != 3)
                return null;

            if (long.TryParse(parts[1], out long expireTime))
            {
                if ((DateTimeOffset.Now.ToUnixTimeSeconds() - expireTime) < (long)(Program.Settings?.ThumbnailCacheExpireTime ?? 30 * 60))
                {
                    bool isNSFW = parts[2].StartsWith("nsfw");
                    return new MemoryThumbnail(modId, await File.ReadAllBytesAsync(Path.Combine(cacheDir.FullName, name)), DateTimeOffset.FromUnixTimeSeconds(expireTime), isNSFW);
                }
                else
                {
                    File.Delete(Path.Combine(cacheDir.FullName, name));
                    return null;
                }
            }
            return null;
        }

        private static long GetExpireFromName(string name)
        {
            var parts = name.Split('-');
            if (parts.Length != 3)
                return -1;

            if (long.TryParse(parts[1], out long expireTime))
                return expireTime;

            return -1;
        }

        private static async Task<MemoryThumbnail?> CacheRemote(long modId)
        {
            var remoteThumbnail = await GetRemoteModThumbnailUrl(modId);
            if (remoteThumbnail is not null)
            {
                var path = await CacheModThumbnail(remoteThumbnail);
                if (path is not null)
                    return new MemoryThumbnail(modId, await File.ReadAllBytesAsync(path), remoteThumbnail.ExpireTime, remoteThumbnail.IsNSFW);
            }
            return null;
        }

        private static async Task<string> CacheModThumbnail(RemoteThumbnailResponse thumbnailResponse)
        {
            DirectoryInfo current = new(Directory.GetCurrentDirectory());
            DirectoryInfo cacheDir = new(Path.Combine(current.FullName, "Cache"));

            if (!cacheDir.Exists)
                cacheDir.Create();

            var path = Path.Combine(
                    cacheDir.FullName,
                    FormatFileName(
                        thumbnailResponse.ModId,
                        DateTimeOffset.Now.AddSeconds((long)(Program.Settings?.ThumbnailCacheExpireTime ?? 30 * 60)).ToUnixTimeSeconds(),
                        thumbnailResponse.IsNSFW)
                    );

            await DownloadFile(thumbnailResponse.ThumbnailUrl, path);
            return path;
        }

        private static string FormatFileName(long modId, long expireTime, bool isNSFW)
        {
            string maturity = isNSFW ? "nsfw" : "sfw";
            return FileFormat.Replace("{mod_id}", modId.ToString())
                             .Replace("{expire_time}", expireTime.ToString())
                             .Replace("{maturity}", maturity);
        }

        private static async Task DownloadFile(string url, string filePath)
        {
            using HttpClient client = new();
            using var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite);
            await response.Content.CopyToAsync(fs);
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

    public class LocalThumbnailResponse(long modId, string file, DateTimeOffset expire, bool isNSFW = false)
    {
        public long ModId { get; set; } = modId;
        public string File { get; set; } = file;

        public bool IsNSFW { get; set; } = isNSFW;

        public DateTimeOffset ExpireTime { get; set; } = expire;
    }

    public class MemoryThumbnail(long modId, byte[] image, DateTimeOffset expire, bool isNSFW = false)
    {
        public long ModId { get; set; } = modId;
        public byte[] Image { get; set; } = image;

        public bool IsNSFW { get; set; } = isNSFW;

        public DateTimeOffset ExpireTime { get; set; } = expire;
    }
}