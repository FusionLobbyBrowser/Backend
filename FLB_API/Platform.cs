using System.Collections.Concurrent;

using FLB_API.Managers;

using FusionAPI;
using FusionAPI.Data.Containers;

using ILogger = FusionAPI.Data.Interfaces.ILogger;

namespace FLB_API
{
    public class Platform
    {
        public PlatformType Type { get; set; }

        public ILogger? Logger { get; set; }

        public Fusion? Client { get; set; }

        public LobbyListResponse? Lobbies { get; set; }

        public Platform(Fusion client, ILogger? logger, PlatformType type)
        {
            Client = client;
            Logger = logger;
            Type = type;
        }

        public Platform(Fusion client, PlatformType type, ILogger.LogLevel level = ILogger.LogLevel.Info)
        {
            Client = client;
            Logger = new Logger(level, Enum.GetName(type) ?? "N/A");
            Type = type;
        }

        public async Task Init(Dictionary<string, string>? metadata = null)
        {
            if (Client != null)
                await Client.Initialize(Logger, metadata);
        }

        public static async Task<Platform> CreateAndInit(Fusion client, PlatformType type, ILogger.LogLevel level = ILogger.LogLevel.Info, Dictionary<string, string>? metadata = null)
        {
            var platform = new Platform(client, type, level);
            await platform.Init(metadata);
            return platform;
        }

        public async Task<CustomLobbyInfo[]> FetchLobbies()
        {
            if (Client?.Handler?.IsInitialized == true)
            {
                Lobbies = await Program.CreateResponse(await Internal_FetchLobbies() ?? [],
                    Client.Handler.LastFetch, Program.Settings?.Interval ?? 30);
                return await Internal_FetchLobbies(true) ?? [];
            }
            else
            {
                Logger?.Warning("Steam Client is not initialized, skipping lobby fetch...");
            }

            return [];
        }

        internal async Task<CustomLobbyInfo[]> Internal_FetchLobbies(bool friendsOnly = false)
        {
            if (Client == null)
                return [];

            Logger?.Info($"Fetching lobbies... {(friendsOnly ? "(Friends Only)" : "(Public)")}");
            LobbyInfo[] lobbies;
            try
            {
                if (!friendsOnly)
                    lobbies = await Client.GetLobbies();
                else
                    lobbies = await Client.GetLobbies(publicLobbies: false, friendsOnlyLobbies: true);
            }
            catch (Exception e)
            {
                Logger?.Error($"Failed to fetch lobbies {(friendsOnly ? "(Friends Only)" : "(Public)")}", e);
                return [];
            }

            var filtered = (await lobbies.Select(x => x.Convert()).Where(async x => Program.Blacklist == null || await Program.Blacklist.IsAllowed(x))).ToArray();
            Logger?.Info($"Successfully fetched lobbies ({filtered.Length} -> {lobbies.Length - filtered.Length} filtered out)... {(friendsOnly ? "(Friends Only)" : "(Public)")}");

            return [.. filtered];
        }
    }

    public enum PlatformType
    {
        All,
        Steam,
        Epic
    }
}