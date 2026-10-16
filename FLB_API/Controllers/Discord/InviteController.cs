using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Mvc;

namespace FLB_API.Controllers.Discord
{
    [ApiController]
    [Route("discord/invite/{inviteCode}")]
    public class InviteController : ControllerBase
    {
        internal static HttpClient HttpClient { get; } = new();

        [HttpGet(Name = "GetInviteInfo")]
        [Tags("Discord")]
        [EndpointSummary("Get info about a Discord invite")]
        [EndpointDescription("This is done to ensure that the CSP is strict and in case of any possible script injection attack there are as few as possible damages")]
        [ProducesResponseType<string>(200, "application/json", Description = "Returns the info about the Discord invite")]
        public async Task<IActionResult> Get(
            [FromRoute(Name = "inviteCode")][Description("The code of the invite that you want to get information about")][Required] string inviteCode)
        {
            inviteCode = Path.GetFileName(inviteCode);

            string invite;
            try
            {
                var request = await HttpClient.GetAsync($"https://discord.com/api/invite/{inviteCode}?with_counts=true");
                invite = await request.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                Program.Logger?.Error(ex, "Failed to retrieve the invite info for " + inviteCode);
                return Problem("No info was found for the specified invite code.", title: "Invite Not Found", statusCode: 404);
            }

            return Ok(invite);
        }
    }
}