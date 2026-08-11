using FLB_API.Controllers.Steam;

using FusionAPI.Data.Containers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FLB_API.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class LobbyListController : ControllerBase
    {
        private const string ContentType = "application/json";

        [HttpGet(Name = "GetPublicLobbies")]
        [Authorize]
        [AllowAnonymous]
        public async Task<IActionResult> GetPublicLobbies([FromQuery(Name = "platform")] string platform = "", [FromQuery(Name = "includeFriendsOnly")] bool friendsOnly = true)
        {
            Platform platformType;
            if (platform.Equals("Steam", StringComparison.OrdinalIgnoreCase))
                platformType = Platform.Steam;
            else if (platform.Equals("Epic", StringComparison.OrdinalIgnoreCase))
                platformType = Platform.Epic;
            else if (string.IsNullOrWhiteSpace(platform))
                platformType = Platform.All;
            else
                return Program.CreateResult("The provided platform does not exist. Leave empty to combine from all available platforms or choose from the following: Steam, Epic", 400);

            if (platformType != Platform.All)
            {
                var handler = platformType == Platform.Steam ? Program.SteamClient : Program.EpicClient;
                if (handler?.Handler.IsInitialized != true)
                    return Program.CreateResult($"Server is not connected to {Enum.GetName(platformType)}.", 500);
            }
            else
            {
                if (Program.SteamClient?.Handler.IsInitialized != true)
                    return Program.CreateResult("Server is not connected to Steam.", 500);
                if (Program.EpicClient?.Handler.IsInitialized != true)
                    return Program.CreateResult("Server is not connected to Epic.", 500);
            }

            Response.Headers.AccessControlExposeHeaders = new Microsoft.Extensions.Primitives.StringValues("Server-Uptime");
            Response.Headers.Append("Server-Uptime", ((DateTimeOffset)Program.Uptime).ToUnixTimeSeconds().ToString());

            var list = platformType switch
            {
                Platform.Steam => Program.SteamLobbies,
                Platform.Epic => Program.EpicLobbies,
                _ => Program.Lobbies
            };

            if (string.IsNullOrWhiteSpace(list?.Json))
                return Program.CreateResult("Did not fetch lobbies yet", 500);

            if (!friendsOnly)
                return Program.CreateResult(list.Json, contentType: ContentType);

            var self = User.GetSteamId();
            if (self == -1 || string.IsNullOrWhiteSpace(Program.FriendsOnlyLobbies?.Json))
                return Program.CreateResult(list.Json, contentType: ContentType);

            List<LobbyInfo> copy = [.. (LobbyInfo[])Program.FriendsOnlyLobbies.Lobbies.Clone()];

            string[] friends = [];
            try
            {
                friends = await FriendsController.GetFriendIDs((ulong)self);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                Program.Logger?.Error(ex, "User has friends list private! Cannot fetch friends only lobbies");
            }

            if (!(friends?.Length > 0))
                return Program.CreateResult(list.Json, contentType: ContentType);

            copy =
            [
                .. copy.Where(l => friends.Any(x => x == l.LobbyID)),
                .. list.Lobbies
            ];

            list = new LobbyListResponse([.. copy], DateTimeOffset.FromUnixTimeSeconds(list.Date), list.Interval, friends);

            return Program.CreateResult(list.Json, contentType: ContentType);
        }

        public enum Platform
        {
            All,
            Steam,
            Epic
        }
    }
}