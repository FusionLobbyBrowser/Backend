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
            Service serviceType;
            if (service.Equals("Steam", StringComparison.OrdinalIgnoreCase))
                serviceType = Service.Steam;
            else if (service.Equals("Epic", StringComparison.OrdinalIgnoreCase))
                serviceType = Service.Epic;
            else
                return NotFound("The provided service does not exist. Choose from the following: Steam, Epic");

            var handler = serviceType == Service.Steam ? Program.FusionClient?.Handler : Program.EOSClient?.Handler;
            if (handler?.IsInitialized != true)
                return StatusCode(500, $"Server is not connected to {Enum.GetName(serviceType)}.");

            Response.Headers.AccessControlExposeHeaders = new Microsoft.Extensions.Primitives.StringValues("Server-Uptime");
            Response.Headers.Append("Server-Uptime", ((DateTimeOffset)Program.Uptime).ToUnixTimeSeconds().ToString() ?? "-1");

            if (serviceType == Service.Steam)
                return Ok(new LobbyListResponse(Program.SteamLobbies ?? [], handler.LastFetch));
            else if (serviceType == Service.Epic)
                return Ok(new LobbyListResponse(Program.EOSLobbies ?? [], handler.LastFetch));
            else
                return NotFound("The provided service does not exist. Choose from the following: Steam, Epic");
        }

        public enum Service
        {
            Steam,
            Epic
        }
    }
}