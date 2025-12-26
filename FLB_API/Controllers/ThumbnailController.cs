using Microsoft.AspNetCore.Mvc;

namespace FLB_API.Controllers
{
    [ApiController]
    [Route("[controller]/{modId}")]
    public class ThumbnailController : ControllerBase
    {
        [HttpGet(Name = "GetThumbnail")]
        public async Task<IActionResult> Get([FromRoute(Name = "modId")] string modId)
        {
            if (string.IsNullOrWhiteSpace(modId))
                return BadRequest("modId is required.");

            if (Program.FusionClient?.Handler.IsInitialized != true)
                return StatusCode(500, "Server is not connected to Steam.");

            if (!long.TryParse(modId, out long _modId))
                return BadRequest("modId is not valid.");

            LocalThumbnailResponse? thumbnail;
            try
            {
                thumbnail = await ModIOManager.GetModThumbnail(_modId);
                if (thumbnail == null)
                    return NotFound("Thumbnail not found.");
            }
            catch (Exception ex)
            {
                Program.Logger?.Error(ex, "Error fetching thumbnail for" + modId);
                return StatusCode(500, "An error occurred while fetching the thumbnail");
            }

            var fileBytes = await System.IO.File.ReadAllBytesAsync(thumbnail.File);

            Response.Headers.AccessControlExposeHeaders = new Microsoft.Extensions.Primitives.StringValues("ModIO-Maturity");
            Response.Headers.Append("ModIO-Maturity", thumbnail.IsNSFW ? "nsfw" : "safe");

            return File(fileBytes, "image/png");
        }
    }
}
