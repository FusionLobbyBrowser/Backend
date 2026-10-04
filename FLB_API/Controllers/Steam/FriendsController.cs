using System.ComponentModel;
using System.Text.Json;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Steam.Models.SteamCommunity;

using SteamWebAPI2.Interfaces;
using SteamWebAPI2.Utilities;

namespace FLB_API.Controllers.Steam
{
    [ApiController]
    [Route("steam/friends")]
    public class FriendsController : ControllerBase
    {
        private const float CacheTime = 60 * 0.5f;

        private static Dictionary<ulong, FriendsCache> Cache { get; } = [];

        [Authorize]
        [HttpGet(Name = "GetSteamFriends")]
        [Tags("Steam")]
        [EndpointSummary("Get the Steam friends of the currently authenticated user")]
        [ProducesResponseType<List<JsonPlayerSummaryModel>>(200, "application/json", Description = "Returns the friends list of the specified user")]
        [ProducesResponseType<ProblemDetails>(501, Description = "Attempted to check a friends list of a user you are not logged in as")]
        [ProducesResponseType<ProblemDetails>(401, Description = "The user is not signed in or has their friends list private")]
        [ProducesResponseType<ProblemDetails>(400, Description = "Steam API returned no friends for such ID")]
        public async Task<IActionResult> Get()
        {
            if (string.IsNullOrWhiteSpace(Program.Settings?.SteamWebApiToken))
                return Problem("Backend is not set up for using Steam API!", title: "Not Set Up", statusCode: 500);

            var id = User.GetSteamId();
            if (User.GetSteamId() == -1)
                return Problem("You must be signed in to check the Steam friends!", title: "Not Signed In", statusCode: 401);

            FriendsCache? friends;
            try
            {
                friends = await GetFriends((ulong)id);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return Problem("The user has the friends list private!", title: "Private Friends List", statusCode: 401);
            }

            if (friends?.Friends == null || friends?.FriendsJson == null)
                return Problem("Steam API returned no friends for such ID!", title: "No Friends", statusCode: 400);

            return Program.CreateResult(friends.FriendsJson, contentType: "application.json");
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

            var factory = new SteamWebInterfaceFactory(Program.Settings!.SteamWebApiToken);
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

            var factory = new SteamWebInterfaceFactory(Program.Settings!.SteamWebApiToken);
            var user = factory.CreateSteamWebInterface<SteamUser>(ProfileController.HttpClient);

            var list = await user.GetFriendsListAsync(id);
            return list?.Data?.Select(x => x.SteamId.ToString())?.ToArray() ?? [];
        }
    }

    public class FriendsCache
    {
        public string? FriendsJson { get; private set; }

        public List<JsonPlayerSummaryModel>? Friends
        {
            get;
            set
            {
                field = value;
                FriendsJson = JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
            }
        }

        public FriendsCache(List<PlayerSummaryModel> friends)
        {
            Friends = friends?.ConvertAll(x => new JsonPlayerSummaryModel(x));
            Start = DateTimeOffset.Now;
        }

        public DateTimeOffset Start { get; }
    }
}