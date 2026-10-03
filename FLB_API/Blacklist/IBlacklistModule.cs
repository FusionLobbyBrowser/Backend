namespace FLB_API.Blacklist
{
    public interface IBlacklistModule
    {
        public string Id { get; }

        public Task<bool> InitAsync(BlacklistManager manager, FusionAPI.Interfaces.ILogger logger);

        public Task<bool> DeInitAsync();

        public Task<bool> IsLobbyAllowed(CustomLobbyInfo info);
    }
}