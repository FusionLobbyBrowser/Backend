using System.Text.Json;
using System.Text.Json.Serialization;

using FusionAPI.Data.Containers;

namespace FLB_API
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    public class LobbyListResponse
    {
        [JsonIgnore]
        public string JSON { get; set; }

        [JsonPropertyName("lobbies")]
        public LobbyInfo[] Lobbies { get; set; }

        [JsonPropertyName("date")]
        public long Date { get; set; }

        [JsonPropertyName("interval")]
        public int Interval { get; set; }

        [JsonConstructor]
        public LobbyListResponse(LobbyInfo[] lobbies, DateTime date, int interval = 30)
        {
            Lobbies = lobbies;
            Date = ((DateTimeOffset)date).ToUnixTimeSeconds();
            Interval = interval;
            JSON = JsonSerializer.Serialize(this, JsonSerializerOptions.Web);
        }
    }
}