using System.Text.Json;
using System.Text.Json.Serialization;

using FusionAPI.Data.Containers;

using Steam.Models.SteamCommunity;

namespace FusionAPI
{
    public class FusionProxy(Uri? host = null)
    {
        public Uri Host { get; set; } = host ?? new("https://fusionapi.hahoos.dev/");

        public HttpClient? Client { get; set; } = new();

        public LobbyInfo[] GetLobbies(string platform = "")
        {
            Client ??= new();

            var url = new Uri(Host, !string.IsNullOrWhiteSpace(platform) ? $"lobbylist?platform={platform}" : "lobbylist");
            var task = Client.GetStringAsync(url);
            task.Wait();
            if (task.IsCompletedSuccessfully)
                return JsonSerializer.Deserialize<LobbyListResponse>(task.Result)?.Lobbies ?? [];

            return [];
        }

        public PlayerSummaryModel? GetSteamProfile(long platformID)
        {
            Client ??= new();

            var url = new Uri(Host, $"steam/profile/{platformID}");
            var task = Client.GetStringAsync(url);
            task.Wait();
            if (task.IsCompletedSuccessfully)
                return JsonSerializer.Deserialize<PlayerSummaryModel>(task.Result);

            return null;
        }

        public Uri GetThumbnailURL(long modId, string barcode = "")
        {
            if (string.IsNullOrWhiteSpace(barcode))
                return new Uri(Host, $"thumbnail/{modId}");
            else
                return new Uri(Host, $"thumbnail/{modId}?barcode={barcode}");
        }
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    public class LobbyListResponse
    {
        [JsonIgnore]
        public string JSON { get; set; }

        [JsonPropertyName("lobbies")]
        public LobbyInfo[] Lobbies { get; set; }

        [JsonPropertyName("date")]
        public long Date { get; set; }

        [JsonPropertyName("interval")]
        public int Interval { get; set; }

        [JsonConstructor]
        public LobbyListResponse(LobbyInfo[] lobbies, DateTime date, int interval = 30)
        {
            Lobbies = lobbies;
            Date = ((DateTimeOffset)date).ToUnixTimeSeconds();
            Interval = interval;
            JSON = JsonSerializer.Serialize(this, JsonSerializerOptions.Web);
        }
    }
}