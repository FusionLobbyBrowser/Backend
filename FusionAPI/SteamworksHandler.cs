using FusionAPI.Data.Enums;
using FusionAPI.Interfaces;

using Steamworks;
using Steamworks.Data;

namespace FusionAPI
{
    public class SteamworksHandler : ISteamHandler
    {
        public bool IsInitialized => SteamClient.IsValid;

        private ILogger? Logger;

        public async Task<IMatchmakingLobby[]> GetLobbies(bool includePrivate = false)
        {
            var lobbies = ConvertLobbies(await GetSteamLobbies(includePrivate));
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

        internal static async Task<Lobby[]> GetSteamLobbies(bool includePrivate = false)
        {
            var list = Steamworks.SteamMatchmaking.LobbyList;
            list.FilterDistanceWorldwide();
            list.WithMaxResults(int.MaxValue);
            list.WithSlotsAvailable(int.MaxValue);
            list.WithKeyValue(LobbyKeys.IdentifierKey, bool.TrueString);
            list.WithKeyValue(LobbyKeys.HasLobbyOpenKey, bool.TrueString);
            list.WithKeyValue(LobbyKeys.GameKey, "BONELAB");
            list.WithNotEqual(LobbyKeys.PrivacyKey, (int)ServerPrivacy.PRIVATE);
            list.WithNotEqual(LobbyKeys.PrivacyKey, (int)ServerPrivacy.LOCKED);
            list.WithNotEqual(LobbyKeys.PrivacyKey, (int)ServerPrivacy.FRIENDS_ONLY);

            return await list.RequestAsync();
        }

        public Task Init(ILogger logger, Dictionary<string, string> metadata)
        {
            Logger = logger;
            Dispatch.OnDebugCallback += SteamworksDebug;
            Dispatch.OnException += SteamworksError;
            Steamworks.SteamClient.Init(Fusion.AppID);
            if (!Steamworks.SteamClient.IsValid)
                throw new InvalidOperationException("Steamworks failed to initialize!");
            return Task.CompletedTask;
        }

        private void SteamworksDebug(CallbackType type, string msg, bool server)
            => Logger?.Trace("[{0}] {1}", server ? "SERVER" : "CLIENT", msg);

        private void SteamworksError(Exception ex)
            => Logger?.Error("Steamworks Exception: {0}", ex);

        public bool IsFriend(ulong id)
            => SteamFriends.GetFriends().Any(f => f.Id == id);
    }

    internal class SteamworksLobby(Lobby lobby) : IMatchmakingLobby
    {
        public ulong Owner => lobby.Owner.Id;

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
