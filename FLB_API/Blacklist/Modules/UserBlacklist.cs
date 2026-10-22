using System.Text.Json;
using System.Text.Json.Serialization;

namespace FLB_API.Blacklist.Modules
{
    public class UserBlacklist : IBlacklistModule
    {
        public string Id => "UserBlacklist";

        public GlobalBanList RemoteList { get; private set; } = new();

        public GlobalBanList LocalList { get; private set; } = new();

        private BlacklistManager? Instance { get; set; }

        private FusionAPI.Data.Interfaces.ILogger? Logger { get; set; }

        private bool _deinitialized = false;

        public const string RepositoryUrl = $"https://raw.githubusercontent.com/Lakatrazz/Fusion-Lists/{Branch}/";

        public const string Branch = "main";

        private HttpClient HttpClient { get; } = new();

        private DateTimeOffset? LastFetch { get; set; } = null;

        public Task<bool> DeInitAsync()
        {
            _deinitialized = true;
            LastFetch = null;
            return Task.FromResult(true);
        }

        public async Task<bool> InitAsync(BlacklistManager manager, FusionAPI.Data.Interfaces.ILogger logger)
        {
            _deinitialized = false;
            Instance = manager;
            Logger = logger;

            _ = Task.Factory.StartNew(async () =>
            {
                while (!_deinitialized)
                {
                    await LoadBans();
                    await Task.Delay(30 * 1000);
                }
            });
            return true;
        }

        public Task<bool> IsLobbyAllowed(CustomLobbyInfo info)
        {
            if (RemoteList?.Bans == null || RemoteList.Bans.Count == 0)
                return Task.FromResult(true);

            return Task.FromResult(Matches(RemoteList, info) && Matches(LocalList, info));
        }

        private static bool Matches(GlobalBanList list, CustomLobbyInfo info)
        {
            return !list.Bans.Any(ban =>
                ban.Games.Any(game => game.Game == "BONELAB")
                && ban.Platforms.Any(platform => platform.Platform == info.LobbyPlatform && platform.PlatformID.ToString() == info.LobbyID));
        }

        public async Task FetchBans()
        {
            try
            {
                Logger?.Info("Fetching global bans...");
                var res = await HttpClient.GetAsync($"{RepositoryUrl}globalBans.json");
                res?.EnsureSuccessStatusCode();
                if (res?.Content == null)
                {
                    Logger?.Error("Failed to fetch global bans: Response or content is null");
                    return;
                }
                var list = await res?.Content?.ReadFromJsonAsync<GlobalBanList>()!;
                if (list != null)
                    RemoteList = list;
                else
                    Logger?.Error("Failed to fetch global bans: Deserialized list is null");

                Logger?.Info($"Successfully fetched global bans! ({RemoteList.Bans.Count} bans)");
            }
            catch (Exception ex)
            {
                Logger?.Error("Failed to fetch global bans", ex);
            }
        }

        private async Task LoadBans()
        {
            try
            {
                if (LastFetch == null || (DateTimeOffset.Now - LastFetch.Value).TotalMinutes > 30)
                {
                    await FetchBans();
                    LastFetch = DateTimeOffset.Now;
                }

                Logger?.Info("Loading blacklisted users...");
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "blacklistedUsers.json");
                if (!File.Exists(path))
                {
                    Logger?.Info("Blacklisted users file is missing, creating...");
                    await using var stream = File.CreateText(path);
                    var serialized = JsonSerializer.Serialize(new GlobalBanList());
                    await stream.WriteAsync(serialized);
                    await stream.FlushAsync();
                    stream.Close();
                }
                else
                {
                    await using var file = File.OpenRead(path);
                    LocalList = await JsonSerializer.DeserializeAsync<GlobalBanList>(file) ?? new GlobalBanList();
                    Logger?.Info($"Successfully loaded blacklisted users! ({LocalList?.Bans?.Count ?? 0} users)");
                }
            }
            catch (Exception ex)
            {
                Logger?.Error("Failed to set blacklisted words from file", ex);
            }
        }
    }

    [Serializable]
    public class GlobalBanInfo
    {
        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; } = null;

        [JsonPropertyName("games")]
        public List<GameInfo> Games { get; set; } = [];

        [JsonPropertyName("platforms")]
        public List<PlatformInfo> Platforms { get; set; } = [];
    }

    [Serializable]
    public class GlobalBanList
    {
        [JsonPropertyName("bans")]
        public List<GlobalBanInfo> Bans { get; set; } = [];
    }

    [Serializable]
    public class GameInfo
    {
        [JsonPropertyName("game")]
        public string? Game { get; set; }
    }

    [Serializable]
    public class PlatformInfo
    {
        [JsonPropertyName("platformID")]
        public ulong PlatformID { get; set; }

        [JsonPropertyName("platform")]
        public string? Platform { get; set; }
    }
}