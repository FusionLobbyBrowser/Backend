using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FLB_API.Controllers.Steam
{
    [ApiController]
    [Route("steam/avatar/{steamId}")]
    public class AvatarController : ControllerBase
    {
        internal static HttpClient HttpClient { get; } = new();

        [HttpGet(Name = "GetSteamAvatar")]
        [Tags("Steam")]
        [EndpointSummary("Get the Steam avatar of a specified user")]
        [EndpointDescription("This is done to ensure that the CSP is strict and in case of any possible script injection attack there are as few as possible damages")]
        [ProducesResponseType<FileStreamResult>(200, "image/png", Description = "Returns the Steam avatar of the specified user")]
        [ProducesResponseType<ProblemDetails>(500, contentType: "application/problem+json", Description = "Backend is not set up for using Steam API")]
        [ProducesResponseType<ProblemDetails>(400, contentType: "application/problem+json", Description = "Invalid Steam ID, must be a number or the Steam API returned no profile for the specified ID")]
        public async Task<IActionResult> Get(
            [FromRoute(Name = "steamId")][Description("Steam ID of the user you want to get the avatar of")][Required] string steamId)
        {
            if (string.IsNullOrWhiteSpace(Program.Settings?.SteamWebApiToken))
                return Problem("Backend is not set up for using Steam API!", title: "Not Set Up", statusCode: 500);

            if (!ulong.TryParse(steamId, out var id))
                return Problem("Invalid Steam ID was provided, must be a number!", title: "Invalid ID", statusCode: 400);

            var profile = await ProfileController.GetProfile(id);
            if (profile?.Profile == null || profile?.ProfileJson == null)
                return Problem("Steam API returned no profile for such ID!", title: "No Profile", statusCode: 400);

            var image = await HttpClient.GetByteArrayAsync(profile.Profile.AvatarFullUrl);
            return File(image, "image/png");
        }
    }
}