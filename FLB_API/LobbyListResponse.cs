using System.Text.Json;
using System.Text.Json.Serialization;

using FusionAPI.Data.Containers;

namespace FLB_API
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    public class LobbyListResponse
    {
        [JsonIgnore]
        public string JSON { get; }

        public LobbyInfo[] Lobbies { get; }

        public long Date { get; }

        public int Interval { get; } = Program.Settings?.Interval ?? 30;

        public LobbyListResponse(LobbyInfo[] lobbies, DateTime date)
        {
            Lobbies = lobbies;
            Date = ((DateTimeOffset)date).ToUnixTimeSeconds();
            JSON = JsonSerializer.Serialize(this, JsonSerializerOptions.Web);
        }
    }
}