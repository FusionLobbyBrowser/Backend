namespace FLB_API.Security
{
    public interface ISecurityModule
    {
        public Task InitAsync();

        public Task<bool> IsLobbyAllowed(CustomLobbyInfo info);
    }
}