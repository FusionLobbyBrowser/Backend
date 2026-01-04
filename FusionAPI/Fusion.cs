using FusionAPI.Data.Containers;
using FusionAPI.Interfaces;

namespace FusionAPI
{
    public class Fusion(ISteamHandler handler)
    {
        public const uint AppID = 250820;

        public ISteamHandler Handler { get; set; } = handler;

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

                if (!metadata.HasLobbyOpen)
                    continue;

                if (!includePrivate && IsPrivate(metadata.LobbyInfo))
                    continue;

                if (metadata.LobbyInfo == null)
                    continue;

                if (!includeFull && metadata.LobbyInfo.PlayerCount == metadata.LobbyInfo.MaxPlayers)
                    continue;

                netLobbies.Add(metadata.LobbyInfo);
            }

            return [.. netLobbies];
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

    }
}