using Microsoft.AspNetCore.Mvc;

namespace FLB_API.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class LobbyListController : ControllerBase
    {
        [HttpGet(Name = "GetServers")]
        public IActionResult Get()
        {
            if (Program.FusionClient?.Handler.IsInitialized != true)
                return StatusCode(500, "Server is not connected to Steam.");

            Response.Headers.AccessControlExposeHeaders = new Microsoft.Extensions.Primitives.StringValues("Server-Uptime");
            Response.Headers.Append("Server-Uptime", ((DateTimeOffset)Program.Uptime).ToUnixTimeSeconds().ToString() ?? "-1");

            return Ok(new LobbyListResponse(Program.Lobbies ?? [], Program.Date, Program.PlayerCount ?? new(0, 0)));
        }
    }
}
