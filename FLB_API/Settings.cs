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

        [JsonPropertyName("auth")]
        public Auth? Authentication { get; set; }
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    public class Auth
    {
        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("password")]
        public string? Password { get; set; }
    }
}
