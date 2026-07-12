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

        public async Task<LobbyInfo[]> GetLobbies(bool includeFull = true, bool publicLobbies = true, bool friendsOnlyLobbies = false)
        {
            if (!Handler.IsInitialized)
                return [];

            var lobbies = await Handler.GetLobbies(publicLobbies, friendsOnlyLobbies);

            List<LobbyInfo> netLobbies = [];

            foreach (var lobby in lobbies)
            {
                if (lobby.IsOwnerMe)
                    continue;

                var metadata = LobbyMetadataInfo.Read(lobby);

                if (!ValidateLobby(metadata, publicLobbies, friendsOnlyLobbies, includeFull))
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

        private static bool ValidateLobby(LobbyMetadataInfo? metadata, bool publicLobbies = true, bool friendsOnlyLobbies = false, bool includeFull = true)
        {
            if (metadata == null)
                return false;

            if (!metadata.HasLobbyOpen)
                return false;

            if (metadata.LobbyInfo == null)
                return false;

            if (metadata.LobbyInfo.Privacy == Data.Enums.ServerPrivacy.PRIVATE || metadata.LobbyInfo.Privacy == Data.Enums.ServerPrivacy.LOCKED)
                return false;

            if(!publicLobbies && metadata.LobbyInfo.Privacy == Data.Enums.ServerPrivacy.PUBLIC)
                return false;

            if(!friendsOnlyLobbies && metadata.LobbyInfo.Privacy == Data.Enums.ServerPrivacy.FRIENDS_ONLY)
                return false;

            if (!includeFull && metadata.LobbyInfo.PlayerCount == metadata.LobbyInfo.MaxPlayers)
                return false;

            return true;
        }

        internal static bool IsTypeNumber(Type type)
            => type == typeof(int) || type == typeof(long) || type == typeof(ulong);
    }
}