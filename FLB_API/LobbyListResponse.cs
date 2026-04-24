using System.Text.Json.Serialization;

using FusionAPI.Data.Containers;

namespace FLB_API
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    public class LobbyListResponse(LobbyInfo[] lobbies, DateTime date)
    {
        public LobbyInfo[] Lobbies { get; set; } = lobbies;

        public long Date { get; set; } = ((DateTimeOffset)date).ToUnixTimeSeconds();

        public int Interval { get; set; } = Program.Settings?.Interval ?? 30;
    }
}