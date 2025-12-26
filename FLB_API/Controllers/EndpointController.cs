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
            if (Program.FusionClient?.Handler.IsInitialized != true)
                return StatusCode(500, "Server is not connected to Steam.");


            return Ok("OK");
        }
    }
}
