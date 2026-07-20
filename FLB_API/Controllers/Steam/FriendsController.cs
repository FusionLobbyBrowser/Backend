using System.Text.Json;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Steam.Models.SteamCommunity;

using SteamWebAPI2.Interfaces;
using SteamWebAPI2.Utilities;

namespace FLB_API.Controllers.Steam
{
    [ApiController]
    [Route("steam/friends/{steamId}")]
    public class FriendsController : ControllerBase
    {
        private const float CacheTime = 60 * 0.5f;

        private static Dictionary<ulong, FriendsCache> Cache { get; } = [];

        [Authorize]
        [HttpGet(Name = "GetSteamFriends")]
        public async Task<IActionResult> Get([FromRoute(Name = "steamId")] string steamId)
        {
            if (string.IsNullOrWhiteSpace(Program.Settings?.SteamWebAPI_Token))
                return Program.CreateResult("Backend is not set up for using Steam API!", 500);

            if (!ulong.TryParse(steamId, out ulong id))
                return Program.CreateResult("Invalid Steam ID! ", 400);

            if ((long)id != User.GetSteamID())
                return Program.CreateResult("You can only check the friends list of the user you are logged in as.", 401);

            FriendsCache? friends;
            try
            {
                friends = await GetFriends(id);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return Program.CreateResult("The user has the friends list private!", 401);
            }

            if (friends?.Friends == null)
                return Program.CreateResult("Steam API returned no friends for such ID!", 400);

            return Ok(friends.FriendsJSON);
        }

        public static async Task<FriendsCache?> GetFriends(ulong id)
        {
            var cache = Cache.FirstOrDefault(x => x.Key == id);
            if (cache.Value?.Friends != null)
            {
                if ((DateTimeOffset.Now - cache.Value.Start).TotalSeconds > CacheTime)
                    Cache.Remove(cache.Key);
                else
                    return cache.Value;
            }

            var factory = new SteamWebInterfaceFactory(Program.Settings!.SteamWebAPI_Token);
            var user = factory.CreateSteamWebInterface<SteamUser>(ProfileController.HttpClient);

            var summaries = await user.GetFriendsListAsync(id);
            var friends = await user.GetPlayerSummariesAsync(summaries.Data.ToList().ConvertAll(x => x.SteamId));
            if (friends?.Data == null)
                return null;

            var cached = new FriendsCache([.. friends.Data]);
            Cache.Add(id, cached);
            return cached;
        }

        public static async Task<string[]> GetFriendIDs(ulong id)
        {
            var cache = Cache.FirstOrDefault(x => x.Key == id);
            if (cache.Value?.Friends != null && !((DateTimeOffset.Now - cache.Value.Start).TotalSeconds > CacheTime))
                return cache.Value.Friends?.Select(x => x.SteamId)?.ToArray() ?? [];

            var factory = new SteamWebInterfaceFactory(Program.Settings!.SteamWebAPI_Token);
            var user = factory.CreateSteamWebInterface<SteamUser>(ProfileController.HttpClient);

            var list = await user.GetFriendsListAsync(id);
            return list?.Data?.Select(x => x.SteamId.ToString())?.ToArray() ?? [];
        }
    }

    public class FriendsCache
    {
        public string? FriendsJSON { get; private set; }

        public List<JSONPlayerSummaryModel>? Friends
        {
            get;
            set
            {
                field = value;
                FriendsJSON = System.Text.Json.JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
            }
        }

        public FriendsCache(List<PlayerSummaryModel> friends)
        {
            Friends = friends?.ConvertAll(x => new JSONPlayerSummaryModel(x));
            Start = DateTimeOffset.Now;
        }

        public DateTimeOffset Start { get; }
    }
}