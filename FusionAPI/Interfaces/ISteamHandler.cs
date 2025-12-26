using Steamworks.Data;

namespace FusionAPI.Interfaces
{
    public interface ISteamHandler
    {
        public bool IsInitialized { get; }

        public Task<IMatchmakingLobby[]> GetLobbies();

        public bool IsFriend(ulong id);

        /// <summary>
        /// Initialize the handler that connects to the Steam API.
        /// </summary>
        /// <param name="metadata">Metadata used in case the handler requires additional info, for example username and password</param>
        public Task Init(ILogger logger, Dictionary<string, string> metadata);

    }
}
