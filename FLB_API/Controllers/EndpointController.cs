using FLB_API;

using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace FLB_API.Controllers
{
    [ApiController]
    [Route("/")]
    public class EndpointController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            Response.Headers.AccessControlExposeHeaders = new Microsoft.Extensions.Primitives.StringValues("Server-Uptime");
            Response.Headers.Append("Server-Uptime", ((DateTimeOffset)Program.Uptime).ToUnixTimeSeconds().ToString() ?? "-1");

            return Program.CreateResult("OK");
        }
    }
}