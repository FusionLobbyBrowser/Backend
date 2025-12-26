using System.Text.Json;

namespace FLB_API
{
    public static class ModIOManager
    {
        public static string Token { get; set; } = "";

        private const long ExpireTime = 30 * 60;

        private const string FileFormat = "{mod_id}-{expire_time}-{maturity}.png";

        private static readonly List<long> Processing = [];

        public static bool GetToken()
        {
            var file = Path.Combine(Directory.GetCurrentDirectory(), "modio_token.txt");
            if (File.Exists(file))
            {
                Token = File.ReadAllText(file).Trim();
                Program.Logger?.Information("Mod.io token loaded from file.");
                return true;
            }
            else
            {
                Program.Logger?.Warning("Mod.io token file not found. Remote mod thumbnails will not be available.");
                return false;
            }
        }

        private static async Task<RemoteThumbnailResponse?> GetRemoteModThumbnailUrl(long modId)
        {
            Program.Logger?.Information($"Remotely fetching mod thumbnail for {modId}");

            if (string.IsNullOrWhiteSpace(Token) && !GetToken())
            {
                Program.Logger?.Warning("Mod.io token is not set. Cannot fetch remote mod thumbnail.");
                return null;
            }

            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.mod.io/v1/games/3809/mods/{modId}");
            request.Headers.Add("Authorization", "Bearer " + Token);
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
            if (Processing.Contains(modId))
            {
                Program.Logger?.Information($"Mod thumbnail for {modId} is already being processed. Waiting...");
                while (Processing.Contains(modId))
                    await Task.Delay(500);
            }

            Processing.Add(modId);
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

                var file = files.FirstOrDefault();
                var info = GetInfo(modId, file?.Name);
                return info ?? await CacheRemote(modId);
            }
            catch (Exception ex)
            {
                Program.Logger?.Error(ex, $"Error getting mod thumbnail for {modId}");
                return null;
            }
            finally { Processing.Remove(modId); }
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
