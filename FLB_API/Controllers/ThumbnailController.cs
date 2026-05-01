using FLB_API.Managers;

using Microsoft.AspNetCore.Mvc;

namespace FLB_API.Controllers
{
    [ApiController]
    [Route("[controller]/{modId}")]
    public class ThumbnailController : ControllerBase
    {
        [HttpGet(Name = "GetThumbnail")]
        public async Task<IActionResult> Get([FromRoute(Name = "modId")] string modId, [FromQuery(Name = "barcode")] string barcode = "")
        {
            if (string.IsNullOrWhiteSpace(modId) && string.IsNullOrWhiteSpace(barcode))
                return BadRequest("modId is required.");

            if (!long.TryParse(modId, out long _modId) && string.IsNullOrWhiteSpace(barcode))
                return BadRequest("modId is not valid.");

            MemoryThumbnail? thumbnail;
            try
            {
                thumbnail = await ModIOManager.GetModThumbnail(_modId, barcode);
                if (thumbnail == null)
                    return NotFound("Thumbnail not found.");
            }
            catch (Exception ex)
            {
                Program.Logger?.Error(ex, $"Error fetching thumbnail for {modId}");
                return StatusCode(500, "An error occurred while fetching the thumbnail");
            }

            Response.Headers.AccessControlExposeHeaders = new Microsoft.Extensions.Primitives.StringValues(["ModIO-Maturity", "Server-Uptime"]);
            Response.Headers.Append("Server-Uptime", ((DateTimeOffset)Program.Uptime).ToUnixTimeSeconds().ToString() ?? "-1");
            Response.Headers.Append("ModIO-Maturity", thumbnail.IsNSFW ? "nsfw" : "safe");

            return File(thumbnail.Image, "image/png");
        }
    }
}