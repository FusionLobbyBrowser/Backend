using System.Text.Json;
using System.Text.Json.Serialization;

using FLB_API.Managers;

using FusionAPI.Data.Containers;

namespace FLB_API
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    public class LobbyListResponse
    {
        [JsonIgnore]
        public string Json { get; set; }

        [JsonPropertyName("lobbies")]
        public CustomLobbyInfo[] Lobbies { get; set; }

        [JsonPropertyName("date")]
        public long Date { get; set; }

        [JsonPropertyName("interval")]
        public int Interval { get; set; }

        [JsonPropertyName("friends")]
        public string[] Friends { get; set; }

        [JsonConstructor]
        public LobbyListResponse(LobbyInfo[] lobbies, DateTime date, int interval = 30, string[]? friends = null)
        {
            Lobbies = [.. lobbies.Select(l => l.Convert())];
            Date = ((DateTimeOffset)date).ToUnixTimeSeconds();
            Interval = interval;
            Friends = friends ?? [];
            Json = JsonSerializer.Serialize(this, JsonSerializerOptions.Web);
        }
    }
}