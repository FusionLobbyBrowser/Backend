using System.Text.Json.Serialization;

namespace FusionAPI.Data.Containers;

[Serializable]
public class PlayerList
{
    [JsonPropertyName("players")]
    public PlayerInfo[] Players { get; set; } = [];

}