using Microsoft.AspNetCore.Mvc;

namespace FLB_API.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class LobbyListController : ControllerBase
    {
        [HttpGet(Name = "GetServers")]
        public IActionResult Get([FromQuery(Name = "service")] string service = "Steam")
        {
            if (Program.FusionClient?.Handler.IsInitialized != true)
                return StatusCode(500, "Server is not connected to Steam.");

            Response.Headers.AccessControlExposeHeaders = new Microsoft.Extensions.Primitives.StringValues("Server-Uptime");
            Response.Headers.Append("Server-Uptime", ((DateTimeOffset)Program.Uptime).ToUnixTimeSeconds().ToString() ?? "-1");

            if (service.Equals("Steam", StringComparison.OrdinalIgnoreCase))
                return Ok(new LobbyListResponse(Program.SteamLobbies ?? [], Program.Date));
            else if (service.Equals("Epic", StringComparison.OrdinalIgnoreCase))
                return Ok(new LobbyListResponse(Program.EOSLobbies ?? [], Program.Date));
            else
                return NotFound("The provided service does not exist. Choose from the following: Steam, Epic");
        }
    }
}