using System.Text.Json.Serialization;

namespace FLB_API
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    public class Settings
    {
        [JsonPropertyName("interval")]
        public int Interval { get; set; }

        [JsonPropertyName("modio_token")]
        public string? ModIO_Token { get; set; }

        [JsonPropertyName("preferences")]
        public Preferences? Preferences { get; set; } = new();

        [JsonPropertyName("auth")]
        public Auth? Authentication { get; set; } = new();

        [JsonPropertyName("imap")]
        public ImapAuth? IMAP { get; set; } = new();
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    public class ImapAuth
    {
        [JsonPropertyName("host")]
        public string? Host { get; set; } = "imap.gmail.com";

        [JsonPropertyName("port")]
        public int Port { get; set; } = 993;

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("password")]
        public string? Password { get; set; }
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    public class Auth
    {
        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("password")]
        public string? Password { get; set; }
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    public class Preferences
    {
        [JsonPropertyName("use")]
        public bool Use { get; set; }

        [JsonPropertyName("logLevel")]
        public string LogLevel_String { get; set; } = "INFO";

        [JsonIgnore]
        public FusionAPI.Interfaces.ILogger.LogLevel LogLevel
        {
            get
            {
                return Enum.Parse<FusionAPI.Interfaces.ILogger.LogLevel>(LogLevel_String);
            }
            set
            {
                var @enum = Enum.GetName(value);
                LogLevel_String = @enum ?? "INFO";
            }
        }

        [JsonPropertyName("authHandler")]
        public string AuthHandler { get; set; } = "STEAMKIT";
    }
}