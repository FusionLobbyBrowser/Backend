using System.Text.Json.Serialization;

using FusionAPI.Data.Containers;

namespace FLB_API
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    public class LobbyListResponse(LobbyInfo[] lobbies, DateTime date, PlayerCount count)
    {
        public LobbyInfo[] Lobbies { get; set; } = lobbies;

        public DateTime Date { get; set; } = date;

        public PlayerCount PlayerCount { get; set; } = count;

        public int Interval { get; set; } = Program.Settings?.Interval ?? 30;
    }

    public class PlayerCount(int players, int lobbies)
    {
        public int Players { get; set; } = players;

        public int Lobbies { get; set; } = lobbies;
    }
}
