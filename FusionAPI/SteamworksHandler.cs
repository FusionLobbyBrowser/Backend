using FusionAPI.Data.Enums;
using FusionAPI.Interfaces;

using Steamworks;
using Steamworks.Data;

namespace FusionAPI
{
    public class SteamworksHandler : IMatchmakingHandler
    {
        public bool IsInitialized => SteamClient.IsValid;

        private ILogger? Logger { get; set; }

        public DateTimeOffset LastFetch { get; private set; } = DateTimeOffset.UtcNow;

        public string ID => "Steam";

        public async Task<IMatchmakingLobby[]> GetLobbies(bool publicLobbies = true, bool friendsOnlyLobbies = false)
        {
            var lobbies = ConvertLobbies(await GetSteamLobbies(publicLobbies, friendsOnlyLobbies));
            LastFetch = DateTimeOffset.UtcNow;
            return [.. lobbies];
        }

        internal static List<SteamworksLobby> ConvertLobbies(Lobby[] lobbies)
        {
            var list = new List<SteamworksLobby>();
            foreach (var lobby in lobbies)
            {
                list.Add(new SteamworksLobby(lobby));
            }
            return list;
        }

        internal static async Task<Lobby[]> GetSteamLobbies(bool publicLobbies = true, bool friendsOnlyLobbies = false)
        {
            var list = SteamMatchmaking.LobbyList;
            list.FilterDistanceWorldwide();
            list.WithMaxResults(int.MaxValue);
            list.WithSlotsAvailable(int.MaxValue);

            list.WithKeyValue(LobbyKeys.IDENTIFIER_KEY, bool.TrueString);
            list.WithKeyValue(LobbyKeys.HAS_LOBBY_OPEN_KEY, bool.TrueString);
            list.WithKeyValue(LobbyKeys.GAME_KEY, "BONELAB");
            if (publicLobbies)
                list.WithEqual(LobbyKeys.PRIVACY_KEY, (int)ServerPrivacy.PUBLIC);
            if (friendsOnlyLobbies)
                list.WithEqual(LobbyKeys.PRIVACY_KEY, (int)ServerPrivacy.FRIENDS_ONLY);

            return await list.RequestAsync();
        }

        public Task Init(ILogger logger, Dictionary<string, string> metadata)
        {
            Logger = logger;
#pragma warning disable IDE0079 // Remove unnecessary suppression, yeah sure this is absolutely fucking unnecessary, not like you're screaming at me for having something I cannot fix
#pragma warning disable S2696
            Dispatch.OnDebugCallback += SteamworksDebug;
            Dispatch.OnException += SteamworksError;
#pragma warning restore S2696, IDE0079
            SteamClient.Init(Fusion.AppID);
            return !SteamClient.IsValid ? throw new InvalidOperationException("Steamworks failed to initialize!") : Task.CompletedTask;
        }

        private void SteamworksDebug(CallbackType type, string msg, bool server)
            => Logger?.Trace("[{0}] {1}", server ? "SERVER" : "CLIENT", msg);

        private void SteamworksError(Exception ex)
            => Logger?.Error("Steamworks Exception: {0}", ex);
    }

    internal class SteamworksLobby(Lobby lobby) : IMatchmakingLobby
    {
        private Lobby lobby = lobby;
        public string Owner => lobby.Owner.Id.ToString();

        public bool IsOwnerMe => lobby.Owner.IsMe;

        public string GetData(string key)
            => lobby.GetData(key);

        public bool TryGetData(string key, out string value)
        {
            value = lobby.GetData(key);
            return !string.IsNullOrWhiteSpace(value);
        }
    }
}