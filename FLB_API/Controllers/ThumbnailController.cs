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
                return Program.CreateResult("modId is required.", 400);

            if (!long.TryParse(modId, out long _modId) && string.IsNullOrWhiteSpace(barcode))
                return Program.CreateResult("modId is not valid.", 400);

            MemoryThumbnail? thumbnail;
            try
            {
                thumbnail = await ModIOManager.GetModThumbnail(_modId, barcode);
                if (thumbnail == null)
                    return Program.CreateResult("Thumbnail not found.", 404);
            }
            catch (Exception ex)
            {
                Program.Logger?.Error(ex, $"Error fetching thumbnail for {modId}");
                return Program.CreateResult("An error occurred while fetching the thumbnail", 500);
            }

            Response.Headers.AccessControlExposeHeaders = new Microsoft.Extensions.Primitives.StringValues(["ModIO-Maturity", "Server-Uptime"]);
            Response.Headers.Append("Server-Uptime", ((DateTimeOffset)Program.Uptime).ToUnixTimeSeconds().ToString() ?? "-1");
            Response.Headers.Append("ModIO-Maturity", thumbnail.IsNSFW ? "nsfw" : "safe");

            return File(thumbnail.Image, "image/png");
        }
    }
}