using Microsoft.AspNetCore.Mvc;

namespace FLB_API.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class LobbyListController : ControllerBase
    {
        [HttpGet(Name = "GetServers")]
        public IActionResult Get([FromQuery(Name = "service")] string service = "")
        {
            Service serviceType;
            if (service.Equals("Steam", StringComparison.OrdinalIgnoreCase))
                serviceType = Service.Steam;
            else if (service.Equals("Epic", StringComparison.OrdinalIgnoreCase))
                serviceType = Service.Epic;
            else if (string.IsNullOrWhiteSpace(service))
                serviceType = Service.Combine;
            else
                return Program.CreateResult("The provided service does not exist. Leave empty to combine from all available services or choose from the following: Steam, Epic", 400);

            if (serviceType != Service.Steam)
            {
                var handler = serviceType == Service.Steam ? Program.FusionClient : Program.EOSClient;
                if (handler?.Handler.IsInitialized != true)
                    return Program.CreateResult($"Server is not connected to {Enum.GetName(serviceType)}.", 500);
            }
            else
            {
                if (Program.FusionClient?.Handler.IsInitialized != true)
                    return Program.CreateResult("Server is not connected to Steam.", 500);
                else if (Program.EOSClient?.Handler.IsInitialized != true)
                    return Program.CreateResult("Server is not connected to Epic.", 500);
            }

            Response.Headers.AccessControlExposeHeaders = new Microsoft.Extensions.Primitives.StringValues("Server-Uptime");
            Response.Headers.Append("Server-Uptime", ((DateTimeOffset)Program.Uptime).ToUnixTimeSeconds().ToString() ?? "-1");

            LobbyListResponse? list;
            if (serviceType == Service.Steam)
                list = Program.SteamLobbies;
            else if (serviceType == Service.Epic)
                list = Program.EOSLobbies;
            else
                list = Program.Lobbies;

            if (string.IsNullOrWhiteSpace(list?.JSON))
                return Program.CreateResult("Did not fetch lobbies yet", 500);

            return Program.CreateResult(list.JSON, contentType: "application/json");
        }

        public enum Service
        {
            Steam,
            Epic,
            Combine
        }
    }
}