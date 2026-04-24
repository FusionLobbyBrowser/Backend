using FusionAPI.Data.Enums;

using System.Text.Json.Serialization;

namespace FusionAPI.Data.Containers;

[Serializable]
public class PlayerInfo()
{
    [JsonPropertyName("platformID")]
    [JsonConverter(typeof(NumberToStringConverter))]
    public string PlatformID { get; set; } = "0";

    [JsonPropertyName("username")]
    public string? Username { get; set; } = null;

    [JsonPropertyName("nickname")]
    public string? Nickname { get; set; } = null;

    [JsonPropertyName("description")]
    public string? Description { get; set; } = null;

    [JsonPropertyName("permissionLevel")]
    public PermissionLevel PermissionLevel { get; set; } = PermissionLevel.DEFAULT;

    [JsonPropertyName("avatarTitle")]
    public string? AvatarTitle { get; set; } = null;

    [JsonPropertyName("avatarModID")]
    public int AvatarModID { get; set; } = -1;
}