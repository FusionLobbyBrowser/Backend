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

        internal Dictionary<string, Tuple<string, DateTimeOffset>> Cache { get; } = [];

        [HttpGet(Name = "GetInviteInfo")]
        [Tags("Discord")]
        [EndpointSummary("Get info about a Discord invite")]
        [EndpointDescription("This is done to ensure that the CSP is strict and in case of any possible script injection attack there are as few as possible damages")]
        [ProducesResponseType<string>(200, "application/json", Description = "Returns the info about the Discord invite")]
        [ProducesResponseType<ProblemDetails>(404, contentType: "application/problem+json", Description = "The Discord API threw an error when searching for the invite")]
        public async Task<IActionResult> Get(
            [FromRoute(Name = "inviteCode")][Description("The code of the invite that you want to get information about")][Required] string inviteCode)
        {
            inviteCode = Path.GetFileName(inviteCode);

            string invite;

            if (Cache.TryGetValue(inviteCode, out var cached) && (DateTimeOffset.Now - cached.Item2).TotalSeconds < 60)
            {
                invite = cached.Item1;
            }
            else
            {
                try
                {
                    var request =
                        await HttpClient.GetAsync($"https://discord.com/api/invite/{inviteCode}?with_counts=true");
                    invite = await request.Content.ReadAsStringAsync();
                    Cache[inviteCode] = new Tuple<string, DateTimeOffset>(invite, DateTimeOffset.Now);
                }
                catch (Exception ex)
                {
                    Program.Logger?.Error(ex, "Failed to retrieve the invite info for " + inviteCode.Replace(Environment.NewLine, ""));
                    return Problem("No info was found for the specified invite code.", title: "Invite Not Found",
                        statusCode: 404);
                }
            }

            return Ok(invite);
        }
    }
}