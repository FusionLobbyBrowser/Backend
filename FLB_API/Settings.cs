using System.Text.Json.Serialization;

using FLB_API.Discord;

namespace FLB_API
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    public class Settings
    {
        [JsonPropertyName("interval")]
        public int Interval { get; set; }

        [JsonPropertyName("log_level")]
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

        [JsonPropertyName("modio")]
        public ModIoSettings? ModIo { get; set; } = new();

        [JsonPropertyName("steam")]
        public Steam? Steam { get; set; } = new();

        [JsonPropertyName("discord")]
        public DiscordSettings? Discord { get; set; } = new();

        [JsonPropertyName("imap")]
        public ImapAuth? Imap { get; set; } = new();

        [JsonPropertyName("setup")]
        public bool Setup { get; set; } = true;

        public static Settings Default => new()
        {
            Interval = 30,
            LogLevelString = "Info",
            ModIo = new ModIoSettings()
            {
                Enabled = true,
                PersonalAccessToken = Token.Empty,
                Token = Token.Empty,
                AccountToken = Token.Empty,
                CacheExpireTime = 30 * 60,
                CleanupInterval = 60 * 60,
                Auth = new ModIoAuth
                {
                    Email = "",
                    Use = false
                }
            },
            Discord = DiscordSettings.Empty,
            Steam = new Steam()
            {
                Handler = string.Empty,
                WebApi = SteamWebApi.Empty,
                SteamKitAuth = new Auth()
                {
                    Username = "",
                    Password = "",
                }
            },
            Imap = new ImapAuth()
            {
                Host = "imap.gmail.com",
                Port = 993,
                Username = "",
                Password = ""
            },
            Setup = false
        };
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    public class Steam
    {
        [JsonPropertyName("web_api")]
        public SteamWebApi? WebApi { get; set; }

        [JsonPropertyName("handler")]
        public string Handler { get; set; } = "SteamKit";

        [JsonPropertyName("steamkit_auth")]
        public Auth? SteamKitAuth { get; set; } = new();
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    public class SteamWebApi
    {
        [JsonPropertyName("web_token")]
        public string? WebToken { get; set; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        public static SteamWebApi Empty => new()
        {
            Enabled = true,
            WebToken = "your-token"
        };

        public bool IsValid() => !string.IsNullOrWhiteSpace(WebToken) && WebToken != "your-token";
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
        public Token? PersonalAccessToken { get; set; }

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
    public class DiscordSettings
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("token")]
        public string? Token { get; set; }

        public bool IsValid() => !string.IsNullOrWhiteSpace(Token) && Token != "your-token" && TokenUtils.TryValidateToken(Token);

        public static DiscordSettings Empty => new()
        {
            Enabled = true,
            Token = "your-token"
        };
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

        public bool IsValid() => !string.IsNullOrWhiteSpace(Host) && Port > 0 && !string.IsNullOrWhiteSpace(Username);
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

        public bool IsEnabled() => Content != "disabled";

        public bool IsValid() => Content == "disabled" || (!string.IsNullOrWhiteSpace(Content) && Content != "your-token" && (ExpireTime == -1 || DateTimeOffset.Now.ToUnixTimeSeconds() < ExpireTime));

        public static Token Empty => new("your-token", -1);

        public static Token Disabled => new("disabled", -1);

        public override bool Equals(object? obj)
        {
            if (obj is Token other)
                return Content == other.Content && ExpireTime == other.ExpireTime;
            else if (obj is string str)
                return Content == str;

            return false;
        }

        public override int GetHashCode()
            => HashCode.Combine(Content, ExpireTime);
    }
}