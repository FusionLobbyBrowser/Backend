using System.Text.Json.Serialization;

namespace FLB_API
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    public class Settings
    {
        [JsonPropertyName("interval")]
        public int Interval { get; set; }

        [JsonPropertyName("modio")]
        public ModIoSettings? ModIo { get; set; }

        [JsonPropertyName("steam_token")]
        public string? SteamWebApiToken { get; set; }

        [JsonPropertyName("discord_bot_token")]
        public string? DiscordBotToken { get; set; }

        [JsonPropertyName("preferences")]
        public Preferences? Preferences { get; set; } = new();

        [JsonPropertyName("auth")]
        public Auth? Authentication { get; set; } = new();

        [JsonPropertyName("imap")]
        public ImapAuth? Imap { get; set; } = new();
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    public class ModIoSettings
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("auth")]
        public ModIoAuth? Auth { get; set; }

        [JsonPropertyName("token")]
        public Token? Token { get; set; }

        [JsonPropertyName("account_token")]
        public Token? AccountToken { get; set; }

        // Collections require a separate token
        [JsonPropertyName("personal_access_token")]
        public string? PersonalAccessToken { get; set; }

        [JsonPropertyName("cache_expire_time")]
        public ulong? CacheExpireTime { get; set; } = 30 * 60;

        [JsonPropertyName("cleanup_interval")]
        public int? CleanupInterval { get; set; } = 60 * 60;
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    public class ModIoAuth
    {
        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("auto_reauthenticate")]
        public bool Use { get; set; }
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

        [JsonPropertyName("launchDiscordBot")]
        public bool LaunchDiscordBot { get; set; }

        [JsonPropertyName("logLevel")]
        public string LogLevelString { get; set; } = "INFO";

        [JsonIgnore]
        public FusionAPI.Data.Interfaces.ILogger.LogLevel LogLevel
        {
            get => Enum.Parse<FusionAPI.Data.Interfaces.ILogger.LogLevel>(LogLevelString);
            set
            {
                var @enum = Enum.GetName(value);
                LogLevelString = @enum ?? "INFO";
            }
        }

        [JsonPropertyName("authHandler")]
        public string AuthHandler { get; set; } = "STEAMKIT";
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    public class Token
    {
        [JsonPropertyName("token")]
        public string? Content { get; set; }

        [JsonPropertyName("expire_time")]
        public long ExpireTime { get; set; }

        public override string ToString()
            => Content ?? string.Empty;

        [JsonConstructor]
        public Token()
        {
        }

        public Token(string token, long expireTime)
        {
            Content = token;
            ExpireTime = expireTime;
        }

        public bool IsValid() => !string.IsNullOrWhiteSpace(Content) && Content != "your-token" && (ExpireTime == -1 || DateTimeOffset.Now.ToUnixTimeSeconds() < ExpireTime);
    }
}