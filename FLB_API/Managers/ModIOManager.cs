using System.Text.Json;
using System.Collections.Concurrent;

namespace FLB_API.Managers
{
    public static class ModIOManager
    {
        private const long ExpireTime = 30 * 60;

        private const string FileFormat = "{mod_id}-{expire_time}-{maturity}.png";

        private static async Task<RemoteThumbnailResponse?> GetRemoteModThumbnailUrl(long modId)
        {
            Program.Logger?.Information($"Remotely fetching mod thumbnail for {modId}");

            if (string.IsNullOrWhiteSpace(Program.Settings?.ModIO_Token) || Program.Settings.ModIO_Token == "your-token")
            {
                Program.Logger?.Warning("Mod.io token is not set. Cannot fetch remote mod thumbnail.");
                return null;
            }

            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.mod.io/v1/games/3809/mods/{modId}");
            request.Headers.Add("Authorization", "Bearer " + Program.Settings.ModIO_Token);
            request.Headers.Add("X-Modio-Platform", "windows");
            request.Headers.Add("Accept", "application/json");
            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var json = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
            bool maturity = json.GetProperty("maturity_option").GetInt16() == 8;
            string? thumbnail = null;

            if (json.TryGetProperty("logo", out var logoElement))
                thumbnail = logoElement.GetProperty("thumb_320x180").GetString();

            if (thumbnail is null)
                return null;
            else
                return new RemoteThumbnailResponse(modId, thumbnail, maturity);
        }

        public static async Task<LocalThumbnailResponse?> GetModThumbnail(long modId)
        {
            try
            {
                Program.Logger?.Information($"Getting mod thumbnail for {modId}");
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
                var info = GetInfo(modId, file?.Name);
                return info ?? await CacheRemote(modId);
            }
            catch (Exception ex)
            {
                Program.Logger?.Error(ex, $"Error getting mod thumbnail for {modId}");
                return null;
            }
        }

        private static LocalThumbnailResponse? GetInfo(long modId, string? name)
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
                if ((DateTimeOffset.Now.ToUnixTimeSeconds() - expireTime) < ExpireTime)
                {
                    bool isNSFW = parts[2].StartsWith("nsfw");
                    return new LocalThumbnailResponse(modId, Path.Combine(cacheDir.FullName, name), isNSFW);
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

        private static async Task<LocalThumbnailResponse?> CacheRemote(long modId)
        {
            var remoteThumbnail = await GetRemoteModThumbnailUrl(modId);
            if (remoteThumbnail is not null)
            {
                var path = await CacheModThumbnail(remoteThumbnail);
                if (path is not null)
                    return new LocalThumbnailResponse(modId, path, remoteThumbnail.IsNSFW);
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
                        DateTimeOffset.Now.AddSeconds(ExpireTime).ToUnixTimeSeconds(),
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
    }

    public class RemoteThumbnailResponse(long modId, string thumbnailUrl, bool isNSFW = false)
    {
        public long ModId { get; set; } = modId;
        public string ThumbnailUrl { get; set; } = thumbnailUrl;

        public bool IsNSFW { get; set; } = isNSFW;
    }

    public class LocalThumbnailResponse(long modId, string file, bool isNSFW = false)
    {
        public long ModId { get; set; } = modId;
        public string File { get; set; } = file;

        public bool IsNSFW { get; set; } = isNSFW;
    }
}