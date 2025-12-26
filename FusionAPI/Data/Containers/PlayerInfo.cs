using FusionAPI.Data.Enums;

using System.Text.Json.Serialization;

namespace FusionAPI.Data.Containers;

[Serializable]
public class PlayerInfo()
{
    [JsonPropertyName("longId")]
    public ulong LongId { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; }

    [JsonPropertyName("nickname")]
    public string Nickname { get; set; }

    [JsonPropertyName("permissionLevel")]
    public PermissionLevel PermissionLevel { get; set; }

    [JsonPropertyName("avatarTitle")]
    public string AvatarTitle { get; set; }

    [JsonPropertyName("avatarModId")]
    public int AvatarModId { get; set; } = -1;
}