using FLB_API.Controllers.Steam;
using FusionAPI.Data.Containers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace FLB_API.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class LobbyListController : ControllerBase
    {
        [HttpGet(Name = "GetPublicLobbies")]
        public IActionResult GetPublicLobbies([FromQuery(Name = "platform")] string platform = "")
        {
            Platform platformType;
            if (platform.Equals("Steam", StringComparison.OrdinalIgnoreCase))
                platformType = Platform.Steam;
            else if (platform.Equals("Epic", StringComparison.OrdinalIgnoreCase))
                platformType = Platform.Epic;
            else if (string.IsNullOrWhiteSpace(platform))
                platformType = Platform.Combine;
            else
                return Program.CreateResult("The provided platform does not exist. Leave empty to combine from all available platforms or choose from the following: Steam, Epic", 400);

            if (platformType != Platform.Steam)
            {
                var handler = platformType == Platform.Steam ? Program.FusionClient : Program.EOSClient;
                if (handler?.Handler.IsInitialized != true)
                    return Program.CreateResult($"Server is not connected to {Enum.GetName(platformType)}.", 500);
            }
            else
            {
                if (Program.FusionClient?.Handler.IsInitialized != true)
                    return Program.CreateResult("Server is not connected to Steam.", 500);
                else if (Program.EOSClient?.Handler.IsInitialized != true)
                    return Program.CreateResult("Server is not connected to Epic.", 500);
            }

            Response.Headers.AccessControlExposeHeaders = new Microsoft.Extensions.Primitives.StringValues("Server-Uptime");
            Response.Headers.Append("Server-Uptime", ((DateTimeOffset)Program.Uptime).ToUnixTimeSeconds().ToString() ?? "-1");
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddMvcCore();
            serviceCollection.AddSingleton<IActionResultExecutor<FileStreamResult>, FileStreamResultExecutor>();

            LobbyListResponse? list;
            if (platformType == Platform.Steam)
                list = Program.SteamLobbies;
            else if (platformType == Platform.Epic)
                list = Program.EOSLobbies;
            else
                list = Program.Lobbies;

            if (string.IsNullOrWhiteSpace(list?.JSON))
                return Program.CreateResult("Did not fetch lobbies yet", 500);

            return Program.CreateResult(list.JSON, contentType: "application/json");
        }

        [HttpGet("friendsonly",Name = "GetFriendsOnlyLobbies")]
        [Authorize]
        public async Task<IActionResult> GetFriendsOnly()
        {
            var self = User.GetSteamID();
            if (self == -1)
                return Program.CreateResult("Not authorized!", 401);

            if (string.IsNullOrWhiteSpace(Program.FriendsOnlyLobbies?.JSON))
                return Program.CreateResult("Did not fetch lobbies yet", 500);

            LobbyInfo[] copy = (LobbyInfo[])Program.FriendsOnlyLobbies.Lobbies.Clone();

            var list = new LobbyListResponse(copy,
                DateTimeOffset.FromUnixTimeSeconds(Program.FriendsOnlyLobbies.Date).DateTime, Program.FriendsOnlyLobbies.Interval);

            FriendsCache? friends;
            try
            {
                friends = await FriendsController.GetFriends((ulong)self);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return Program.CreateResult("The user has the friends list private!", 401);
            }

            if (friends?.Friends == null)
                return Program.CreateResult("Steam API returned no friends for such ID!", 400);

            list.Lobbies = [.. list.Lobbies.Where(l => friends.Friends.Any(x=>x.SteamId==l.LobbyID))];


            return Program.CreateResult(list.JSON, contentType: "application/json");
        }

        public enum Platform
        {
            Steam,
            Epic,
            Combine
        }
    }
}