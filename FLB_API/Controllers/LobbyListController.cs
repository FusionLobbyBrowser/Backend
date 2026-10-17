using System.ComponentModel;
using System.Text.Json;

using FLB_API.Controllers.Steam;

using FusionAPI.Data.Containers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FLB_API.Controllers
{
    [ApiController]
    [Route("lobby")]
    public class LobbyListController : ControllerBase
    {
        private const string ContentType = "application/json";

        [HttpGet("all", Name = "GetLobbies")]
        [Tags("Lobbies")]
        [Authorize]
        [AllowAnonymous]
        [EndpointSummary("Get LabFusion lobbies")]
        [ProducesResponseType<LobbyListResponse>(200, "application/json", Description = "Returns a list of lobbies with the requested filters taken into consideration")]
        [ProducesResponseType<ProblemDetails>(500, Description = "The server is not able to fetch the lobbies from the third part provider or is in the process of doing so")]
        public async Task<IActionResult> GetLobbies(
            [FromQuery(Name = "platform")][Description("The provider to fetch the lobbies from. Leave empty to combine from all")][DefaultValue(Platform.All)] Platform platform = Platform.All,
            [FromQuery(Name = "includeFriendsOnly")][Description("Should friends only lobbies be considered. This will only additionally include lobbies hosted by the authorized user's friends")] bool friendsOnly = true)
        {
            var (list, error) = await Lobbies(platform, friendsOnly);
            if (error != null) return error;
            else return Program.CreateResult(list!.Json, contentType: ContentType);
        }

        [HttpGet("query/{search}", Name = "GetLobby")]
        [Tags("Lobbies")]
        [Authorize]
        [AllowAnonymous]
        [EndpointSummary("Get a specified LabFusion lobby")]
        [ProducesResponseType<CustomLobbyInfo>(200, "application/json", Description = "Returns the specified lobby if found")]
        [ProducesResponseType<ProblemDetails>(500, Description = "The server is not able to fetch the lobbies from the third part provider or is in the process of doing so")]
        public async Task<IActionResult> GetLobby(
            [FromRoute(Name = "search")][Description("The search query to use for finding the lobby")] string search,
            [FromQuery(Name = "platform")][Description("The provider to fetch the lobbies from. Leave empty to combine from all")][DefaultValue(Platform.All)] Platform platform = Platform.All,
            [FromQuery(Name = "searchType")][Description("The type of search to perform")][DefaultValue(SearchType.ID)] SearchType searchType = SearchType.ID,
            [FromQuery(Name = "includeFriendsOnly")][Description("Should friends only lobbies be considered. This will only additionally include lobbies hosted by the authorized user's friends")] bool friendsOnly = true)
        {
            var (list, error) = await Lobbies(platform, friendsOnly);
            if (error != null) return error;

            CustomLobbyInfo? info = null;
            if (searchType is SearchType.Code or SearchType.All)
                info = list!.Lobbies?.FirstOrDefault(l => l.LobbyCode == search);

            if (info == null && (searchType is SearchType.ID or SearchType.All))
                info = list!.Lobbies?.FirstOrDefault(l => l.LobbyID == search);

            if (info != null)
                return Program.CreateResult(JsonSerializer.Serialize(info, JsonSerializerOptions.Web), contentType: "application/json");

            return Problem("Lobby not found.", title: "Not Found", statusCode: 404);
        }

        private async Task<Tuple<LobbyListResponse?, ActionResult?>> Lobbies(Platform platform, bool friendsOnly = true)
        {
            if (platform != Platform.All)
            {
                var handler = platform == Platform.Steam ? Program.SteamClient : Program.EpicClient;
                if (handler?.Handler.IsInitialized != true)
                    return new(null, Problem($"Server is not connected to {Enum.GetName(platform)}.", title: "Not Connected", statusCode: 500));
            }
            else
            {
                if (Program.SteamClient?.Handler.IsInitialized != true)
                    return new(null, Problem("Server is not connected to Steam.", title: "Not Connected", statusCode: 500));
                if (Program.EpicClient?.Handler.IsInitialized != true)
                    return new(null, Problem("Server is not connected to Epic.", title: "Not Connected", statusCode: 500));
            }

            Response.Headers.AccessControlExposeHeaders = new Microsoft.Extensions.Primitives.StringValues("Server-Uptime");
            Response.Headers.Append("Server-Uptime", ((DateTimeOffset)Program.Uptime).ToUnixTimeSeconds().ToString());

            var list = platform switch
            {
                Platform.Steam => Program.SteamLobbies,
                Platform.Epic => Program.EpicLobbies,
                _ => Program.Lobbies
            };

            if (string.IsNullOrWhiteSpace(list?.Json))
                return new(null, Problem("Did not fetch lobbies yet", title: "No Lobbies", statusCode: 500));

            if (!friendsOnly)
                return new(list, null);

            var self = User.GetSteamId();
            if (self == -1 || string.IsNullOrWhiteSpace(Program.FriendsOnlyLobbies?.Json))
                return new(list, null);

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
                return new(list, null);

            copy =
            [
                .. copy.Where(l => friends.Any(x => x == l.LobbyID)),
                .. list.Lobbies
            ];

            list = await Program.CreateResponse([.. copy], DateTimeOffset.FromUnixTimeSeconds(list.Date), list.Interval, friends);
            return new(list, null);
        }

        public enum Platform
        {
            All,
            Steam,
            Epic
        }

        public enum SearchType
        {
            ID,
            Code,
            All
        }
    }
}