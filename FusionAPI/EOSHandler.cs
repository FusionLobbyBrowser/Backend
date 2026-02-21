using FusionAPI.Interfaces;

namespace FusionAPI
{
    internal class EOSHandler : IMatchmakingHandler
    {
        public bool IsInitialized => throw new NotImplementedException();

        public Task<IMatchmakingLobby[]> GetLobbies(bool includePrivate = false)
        {
            throw new NotImplementedException();
        }

        public Task Init(ILogger logger, Dictionary<string, string> metadata)
        {
            throw new NotImplementedException();
        }

        public bool IsFriend(string id)
        {
            throw new NotImplementedException();
        }
    }
}