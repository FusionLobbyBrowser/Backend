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
                return Program.CreateResult("The provided service does not exist. Choose from the following: Steam, Epic", 400);

            var handler = serviceType == Service.Steam ? Program.FusionClient : Program.EOSClient;
            if (handler?.Handler.IsInitialized != true)
                return Program.CreateResult($"Server is not connected to {Enum.GetName(serviceType)}.", 500);

            Response.Headers.AccessControlExposeHeaders = new Microsoft.Extensions.Primitives.StringValues("Server-Uptime");
            Response.Headers.Append("Server-Uptime", ((DateTimeOffset)Program.Uptime).ToUnixTimeSeconds().ToString() ?? "-1");

            var list = serviceType == Service.Steam ? Program.SteamLobbies : Program.EOSLobbies;

            if (string.IsNullOrWhiteSpace(list?.JSON))
                return Program.CreateResult("Did not fetch lobbies yet", 500);

            return Program.CreateResult(list.JSON, contentType: "application/json");
        }

        public enum Service
        {
            Steam,
            Epic
        }
    }
}