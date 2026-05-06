namespace FusionAPI.Interfaces
{
    public interface IMatchmakingHandler
    {
        public string ID { get; }

        public bool IsInitialized { get; }

        public DateTime LastFetch { get; }

        public Task<IMatchmakingLobby[]> GetLobbies(bool includePrivate = false);

        public bool IsFriend(string id);

        /// <summary>
        /// Initialize the handler that connects to the Steam API.
        /// </summary>
        /// <param name="metadata">
        /// Metadata used in case the handler requires additional info, for example username and password
        /// </param>
        public Task Init(ILogger logger, Dictionary<string, string> metadata);
    }
}