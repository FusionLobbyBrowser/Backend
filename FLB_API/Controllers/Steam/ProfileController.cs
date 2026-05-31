using System.Text.Json;

using Microsoft.AspNetCore.Mvc;

using Newtonsoft.Json;

using Steam.Models.SteamCommunity;

using SteamWebAPI2.Interfaces;
using SteamWebAPI2.Utilities;

namespace FLB_API.Controllers.Steam
{
    [ApiController]
    [Route("steam/profile/{steamId}")]
    public class ProfileController : ControllerBase
    {
        private const int CacheTime = 60 * 30;

        private static HttpClient HttpClient { get; } = new();

        private static List<ProfileCache> Cache { get; } = [];

        [HttpGet(Name = "GetSteamProfile")]
        public async Task<IActionResult> Get([FromRoute(Name = "steamId")] string steamId)
        {
            if (string.IsNullOrWhiteSpace(Program.Settings?.SteamWebAPI_Token))
                return Program.CreateResult("Backend is not set up for using Steam API!", 500);

            if (!ulong.TryParse(steamId, out ulong id))
                return Program.CreateResult("Invalid Steam ID! ", 400);

            var cache = Cache.FirstOrDefault(x => x.Profile?.SteamId == id);
            if (cache?.Profile != null)
            {
                if ((DateTimeOffset.Now - cache.Start).TotalSeconds > CacheTime)
                {
                    Cache.Remove(cache);
                }
                else
                {
                    return
                        !string.IsNullOrWhiteSpace(cache.ProfileJSON) ?
                        Program.CreateResult(cache.ProfileJSON, contentType: "application/json")
                        : Ok(cache.Profile);
                }
            }

            var factory = new SteamWebInterfaceFactory(Program.Settings!.SteamWebAPI_Token);
            var user = factory.CreateSteamWebInterface<SteamUser>(HttpClient);

            var summaries = await user.GetPlayerSummariesAsync([id]);
            var profile = summaries.Data.FirstOrDefault();
            if (profile == null)
                return Program.CreateResult("Steam API returned no profile for such ID!", 400);

            Cache.Add(new(profile));
            return Ok(profile);
        }
    }

    public class ProfileCache
    {
        public string? ProfileJSON { get; private set; }

        public PlayerSummaryModel? Profile
        {
            get;
            set
            {
                field = value;
                ProfileJSON = System.Text.Json.JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
            }
        }

        public ProfileCache(PlayerSummaryModel profile)
        {
            Profile = profile;
            Start = DateTimeOffset.Now;
        }

        public DateTimeOffset Start { get; }
    }
}