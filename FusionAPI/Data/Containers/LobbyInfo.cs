using System.Text.Json.Serialization;

using FusionAPI.Data.Enums;

namespace FusionAPI.Data.Containers;

[Serializable]
public class LobbyInfo
{
    public static readonly LobbyInfo Empty = new();

    // Info
    [JsonPropertyName("lobbyId")]
    public ulong LobbyId { get; set; }

    [JsonPropertyName("lobbyCode")]
    public string LobbyCode { get; set; }

    [JsonPropertyName("lobbyName")]
    public string LobbyName { get; set; }

    [JsonPropertyName("lobbyDescription")]
    public string LobbyDescription { get; set; }

    [JsonPropertyName("lobbyVersion")]
    public Version LobbyVersion { get; set; }

    [JsonPropertyName("lobbyHostName")]
    public string LobbyHostName { get; set; }

    [JsonPropertyName("playerCount")]
    public int PlayerCount { get; set; }

    [JsonPropertyName("playerList")]
    public PlayerList PlayerList { get; set; }

    // Location
    [JsonPropertyName("levelTitle")]
    public string LevelTitle { get; set; }

    [JsonPropertyName("levelBarcode")]
    public string LevelBarcode { get; set; }

    [JsonPropertyName("levelModId")]
    public int LevelModId { get; set; } = -1;

    // Gamemode
    [JsonPropertyName("gamemodeTitle")]
    public string GamemodeTitle { get; set; }

    [JsonPropertyName("gamemodeBarcode")]
    public string GamemodeBarcode { get; set; }

    // Settings
    [JsonPropertyName("nameTags")]
    public bool NameTags { get; set; }

    [JsonPropertyName("privacy")]
    public ServerPrivacy Privacy { get; set; }

    [JsonPropertyName("slowMoMode")]
    public TimeScaleMode SlowMoMode { get; set; }

    [JsonPropertyName("maxPlayers")]
    public int MaxPlayers { get; set; }

    [JsonPropertyName("voiceChat")]
    public bool VoiceChat { get; set; }

    [JsonPropertyName("playerConstraining")]
    public bool PlayerConstraining { get; set; }

    [JsonPropertyName("mortality")]
    public bool Mortality { get; set; }

    [JsonPropertyName("friendlyFire")]
    public bool FriendlyFire { get; set; }

    [JsonPropertyName("knockout")]
    public bool Knockout { get; set; }

    [JsonPropertyName("knockoutLength")]
    public int KnockoutLength { get; set; }

    // Permissions
    [JsonPropertyName("devTools")]
    public PermissionLevel DevTools { get; set; }

    [JsonPropertyName("constrainer")]
    public PermissionLevel Constrainer { get; set; }

    [JsonPropertyName("customAvatars")]
    public PermissionLevel CustomAvatars { get; set; }

    [JsonPropertyName("kicking")]
    public PermissionLevel Kicking { get; set; }

    [JsonPropertyName("banning")]
    public PermissionLevel Banning { get; set; }

    [JsonPropertyName("teleportation")]
    public PermissionLevel Teleportation { get; set; }
}