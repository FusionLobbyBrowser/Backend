using FusionAPI.Data.Containers;
using FusionAPI.Interfaces;

namespace FusionAPI
{
    public class Fusion(IMatchmakingHandler handler)
    {
        public const uint AppID = 250820;

        public IMatchmakingHandler Handler { get; set; } = handler;

        public Dictionary<string, long> LobbiesUptime { get; private set; } = [];

        public async Task Initialize(ILogger logger, Dictionary<string, string> metadata)
        {
            await Handler.Init(logger, metadata);
        }

        public async Task<LobbyInfo[]> GetLobbies(bool includeFull = true, bool includePrivate = false, bool includeSelf = false)
        {
            if (!Handler.IsInitialized)
                return [];

            // Fetch lobbies
            var lobbies = await Handler.GetLobbies(includePrivate);

            List<LobbyInfo> netLobbies = [];

            foreach (var lobby in lobbies)
            {
                // Make sure this is not us
                if (lobby.IsOwnerMe && !includeSelf)
                    continue;

                var metadata = LobbyMetadataInfo.Read(lobby);

                if (!ValidateLobby(metadata, includePrivate, includeFull))
                    continue;

                long uptime;
                if (!LobbiesUptime.TryGetValue(metadata.LobbyInfo!.LobbyID, out long value))
                {
                    var _uptime = DateTimeOffset.Now.ToUnixTimeSeconds();
                    LobbiesUptime.Add(metadata.LobbyInfo.LobbyID, _uptime);
                    uptime = _uptime;
                }
                else
                {
                    uptime = value;
                }

                metadata.LobbyInfo.LobbyUptime = uptime;
                metadata.LobbyInfo.LobbyPlatform = Handler.ID;

                netLobbies.Add(metadata.LobbyInfo);
            }

            LobbiesUptime = LobbiesUptime.Where(y => netLobbies.Any(x => x.LobbyID == y.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);

            return [.. netLobbies];
        }

        private bool ValidateLobby(LobbyMetadataInfo? metadata, bool includePrivate, bool includeFull)
        {
            if (metadata == null)
                return false;

            if (!metadata.HasLobbyOpen)
                return false;

            if (!includePrivate && IsPrivate(metadata.LobbyInfo))
                return false;

            if (metadata.LobbyInfo == null)
                return false;

            if (!includeFull && metadata.LobbyInfo.PlayerCount == metadata.LobbyInfo.MaxPlayers)
                return false;

            return true;
        }

        public LobbyInfo[] FilterLobbies(LobbyInfo[] lobbies, bool includeFull = true, bool includePrivate = false)
        {
            return [.. lobbies.Where(lobby =>
            {
                if (!includePrivate && IsPrivate(lobby))
                    return false;
                if (!includeFull && lobby.PlayerCount == lobby.MaxPlayers)
                    return false;
                return true;
            })];
        }

        public bool IsPrivate(LobbyInfo? lobby)
        {
            if (lobby == null)
                return true;

            if (lobby.Privacy == Data.Enums.ServerPrivacy.PRIVATE)
                return true;

            if (lobby.Privacy == Data.Enums.ServerPrivacy.LOCKED)
                return true;

            if (lobby.Privacy == Data.Enums.ServerPrivacy.FRIENDS_ONLY && !Handler.IsFriend(lobby.LobbyID))
                return true;

            return false;
        }

        internal static bool IsTypeNumber(Type type)
            => type == typeof(int) || type == typeof(long) || type == typeof(ulong);
    }
}