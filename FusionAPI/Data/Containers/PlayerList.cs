using System.Text.Json.Serialization;

namespace FusionAPI.Data.Containers;

[Serializable]
public class PlayerList
{
    [JsonPropertyName("players")]
    public PlayerInfo[] Players { get; set; } = [];

    [JsonConstructor]
    public PlayerList()
    {
    }

    public PlayerList(PlayerList old)
    {
        Players = (PlayerInfo[])old.Players.Clone();
    }
}