using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Mvc;

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
        [ProducesResponseType<ProblemDetails>(400, contentType: "application/problem+json", Description = "Invalid Steam ID, must be a number or the Steam API returned no profile for the specified ID")]
        [ProducesResponseType<ProblemDetails>(500, contentType: "application/problem+json", Description = "Backend is not properly set up for using Steam API")]
        [ProducesResponseType<ProblemDetails>(501, contentType: "application/problem+json", Description = "Feature is disabled")]
        public async Task<IActionResult> Get(
            [FromRoute(Name = "steamId")][Description("Steam ID of the user you want to get the avatar of")][Required] string steamId)
        {
            if (Program.Settings?.Steam?.WebApi?.Enabled == false)
                return Problem("The server does not support fetching Steam profiles (which is required to get the Steam avatar) as the functionality has been disabled by the person hosting", statusCode: 501, title: "Feature Disabled");

            if (Program.Settings?.Steam?.WebApi?.IsValid() != true)
                return Problem("Backend is not properly set up for using Steam Web API!", statusCode: 500, title: "Not Set Up");

            if (!ulong.TryParse(steamId, out var id))
                return Problem("Invalid Steam ID was provided, must be a number!", statusCode: 400, title: "Invalid ID");

            var profile = await ProfileController.GetProfile(id);
            if (profile?.Profile == null || profile?.ProfileJson == null)
                return Problem("Steam API returned no profile for such ID!", statusCode: 400, title: "No Profile");

            var image = await HttpClient.GetByteArrayAsync(profile.Profile.AvatarFullUrl);
            return File(image, "image/png");
        }
    }
}