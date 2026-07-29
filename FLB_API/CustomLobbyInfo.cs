using System.Text.Json.Serialization;

using FusionAPI.Data.Containers;

namespace FLB_API
{
    public class CustomLobbyInfo(LobbyInfo info, bool lobbyHasFurries) : LobbyInfo(info)
    {
        [JsonPropertyName("lobbyHasFurries")]
        public bool LobbyHasFurries { get; set; } = lobbyHasFurries;
    }
}