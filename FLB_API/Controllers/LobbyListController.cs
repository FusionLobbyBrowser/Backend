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
        [ProducesResponseType<ProblemDetails>(500, contentType: "application/problem+json", Description = "The server is not able to fetch the lobbies from the third part provider or is in the process of doing so")]
        public async Task<IActionResult> GetLobbies(
            [FromQuery(Name = "platform")][Description("The provider to fetch the lobbies from. Leave empty to combine from all")][DefaultValue(PlatformType.All)] PlatformType platform = PlatformType.All,
            [FromQuery(Name = "includeFriendsOnly")][Description("Should friends only lobbies be considered. This will only additionally include lobbies hosted by the authorized user's friends")] bool friendsOnly = true)
        {
            var (list, error) = await Lobbies(platform, friendsOnly);
            return error ?? Program.CreateResult(list!.Json, contentType: ContentType);
        }

        [HttpGet("query/{search}", Name = "GetLobby")]
        [Tags("Lobbies")]
        [Authorize]
        [AllowAnonymous]
        [EndpointSummary("Get a specified LabFusion lobby")]
        [ProducesResponseType<CustomLobbyInfo>(200, "application/json", Description = "Returns the specified lobby if found")]
        [ProducesResponseType<ProblemDetails>(500, contentType: "application/problem+json", Description = "The server is not able to fetch the lobbies from the third part provider or is in the process of doing so")]
        public async Task<IActionResult> GetLobby(
            [FromRoute(Name = "search")][Description("The search query to use for finding the lobby")] string search,
            [FromQuery(Name = "platform")][Description("The provider to fetch the lobbies from. Leave empty to combine from all")][DefaultValue(PlatformType.All)] PlatformType platform = PlatformType.All,
            [FromQuery(Name = "searchType")][Description("The type of search to perform")][DefaultValue(SearchType.Id)] SearchType searchType = SearchType.Id,
            [FromQuery(Name = "includeFriendsOnly")][Description("Should friends only lobbies be considered. This will only additionally include lobbies hosted by the authorized user's friends")] bool friendsOnly = true)
        {
            var (list, error) = await Lobbies(platform, friendsOnly);
            if (error != null) return error;

            CustomLobbyInfo? info = null;
            if (searchType is SearchType.Code or SearchType.All)
                info = list!.Lobbies?.FirstOrDefault(l => l.LobbyCode == search);

            if (info == null && (searchType is SearchType.Id or SearchType.All))
                info = list!.Lobbies?.FirstOrDefault(l => l.LobbyID == search);

            if (info != null)
                return Program.CreateResult(JsonSerializer.Serialize(info, JsonSerializerOptions.Web), contentType: "application/json");

            return Problem("Lobby not found.", statusCode: 404, title: "Not Found");
        }

        private async Task<Tuple<LobbyListResponse?, ActionResult?>> Lobbies(PlatformType platform, bool friendsOnly = true)
        {
            LobbyListResponse? list = null;
            if (platform != PlatformType.All)
            {
                var handler = Program.Platforms.FirstOrDefault(x => x.Type == platform);
                if (handler?.Client?.Handler.IsInitialized != true)
                {
                    return new Tuple<LobbyListResponse?, ActionResult?>(null,
                        Problem($"Server is not connected to {Enum.GetName(platform)}.", statusCode: 500,
                            title: "Not Connected"));
                }

                list = handler?.Lobbies;
            }
            else
            {
                if (Program.Platforms.All(x => x.Client?.Handler.IsInitialized != true))
                {
                    return new Tuple<LobbyListResponse?, ActionResult?>
                    (null,
                        Problem(
                            $"Server is not connected to any of the available platforms ({string.Join(", ", Program.Platforms.Select(x => Enum.GetName(x.Type)))}).",
                            statusCode: 500, title: "Not Connected"));
                }

                list = Program.Lobbies;
            }

            if (string.IsNullOrWhiteSpace(list?.Json))
                return new Tuple<LobbyListResponse?, ActionResult?>(null, Problem("Did not fetch lobbies yet", statusCode: 500, title: "No Lobbies"));

            if (!friendsOnly)
                return new Tuple<LobbyListResponse?, ActionResult?>(list, null);

            var self = User.GetSteamId();
            if (self == -1 || string.IsNullOrWhiteSpace(Program.FriendsOnlyLobbies?.Json))
                return new Tuple<LobbyListResponse?, ActionResult?>(list, null);

            List<CustomLobbyInfo> copy = [.. (CustomLobbyInfo[])Program.FriendsOnlyLobbies.Lobbies.Clone()];

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
                return new Tuple<LobbyListResponse?, ActionResult?>(list, null);

            copy =
            [
                .. copy.Where(l => friends.Any(x => x == l.LobbyID)),
                .. list.Lobbies
            ];

            list = await Program.CreateResponse([.. copy], DateTimeOffset.FromUnixTimeSeconds(list.Date), list.Interval, friends);
            return new Tuple<LobbyListResponse?, ActionResult?>(list, null);
        }

        public enum SearchType
        {
            Id,
            Code,
            All
        }
    }
}