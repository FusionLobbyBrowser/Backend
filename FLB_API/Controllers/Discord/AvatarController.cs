using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Mvc;

namespace FLB_API.Controllers.Discord
{
    [ApiController]
    [Route("discord/guild/{guildId}/icon/{iconHash}")]
    public class AvatarController : ControllerBase
    {
        internal static HttpClient HttpClient { get; } = new();

        [HttpGet(Name = "GetDiscordGuildIcon")]
        [Tags("Discord")]
        [EndpointSummary("Get the Discord icon of a specified guild")]
        [EndpointDescription("This is done to ensure that the CSP is strict and in case of any possible script injection attack there are as few as possible damages")]
        [ProducesResponseType<FileStreamResult>(200, "image/webp", Description = "Returns the Discord icon of the specified guild")]
        [ProducesResponseType<ProblemDetails>(400, contentType: "application/problem+json", Description = "Invalid Guild ID, must be a number or the Discord API returned no icon for the specified ID")]
        public async Task<IActionResult> Get(
            [FromRoute(Name = "guildId")][Description("Guild ID of the guild you want to get the icon of")][Required] string guildId,
            [FromRoute(Name = "iconHash")][Description("Hash of the icon you want to get")][Required] string iconHash,
            [FromQuery(Name = "size")][Description("Size of the icon you want to get")][DefaultValue(256)][Range(16, 2048)] int size = 256,
            [FromQuery(Name = "animated")][Description("Whether the icon should be returned in an animated format if possible")][DefaultValue(true)] bool animated = true)
        {
            if (!ulong.TryParse(guildId, out var id))
                return Problem("Invalid Guild ID was provided, must be a number!", statusCode: 400, title: "Invalid ID");

            var url = CdnUrl(id, Path.GetFileName(iconHash), size, animated);

            byte[] image;
            try
            {
                image = await HttpClient.GetByteArrayAsync(url);
            }
            catch (Exception ex)
            {
                Program.Logger?.Error(ex, "Failed to retrieve the icon for " + guildId.Replace(Environment.NewLine, "") + " guild.");
                return Problem("No icon was found for the specified guild and icon hash.", statusCode: 404, title: "Icon Not Found");
            }

            return File(image, "image/webp");
        }

        private static string CdnUrl(ulong id, string hash, int size = 256, bool animated = true) => $"https://cdn.discordapp.com/icons/{id}/{hash}.webp?size={size}&animated={animated}";
    }
}