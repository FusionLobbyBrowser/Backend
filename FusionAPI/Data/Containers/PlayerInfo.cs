using FusionAPI.Data.Enums;

using System.Text.Json.Serialization;

namespace FusionAPI.Data.Containers;

[Serializable]
public class PlayerInfo()
{
    [JsonPropertyName("platformID")]
    public ulong PlatformID { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("nickname")]
    public string? Nickname { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("permissionLevel")]
    public PermissionLevel PermissionLevel { get; set; }

    [JsonPropertyName("avatarTitle")]
    public string? AvatarTitle { get; set; }

    [JsonPropertyName("avatarModID")]
    public int AvatarModID { get; set; } = -1;
}