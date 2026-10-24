namespace FusionAPI.Data.Interfaces
{
    public interface IMatchmakingHandler
    {
        public string ID { get; }

        public bool IsInitialized { get; }

        public virtual bool SupportsFriendsOnly => true;

        public DateTimeOffset LastFetch { get; }

        public ILogger? Logger { get; }

        public Task<IMatchmakingLobby[]> GetLobbies(bool publicLobbies = true, bool friendsOnlyLobbies = false);

        /// <summary>
        /// Initialize the handler that connects to the Steam API.
        /// </summary>
        /// <param name="metadata">
        /// Metadata used in case the handler requires additional info, for example username and password
        /// </param>
        public Task Init(ILogger? logger, Dictionary<string, string> metadata);

        public Task DeInit();
    }
}