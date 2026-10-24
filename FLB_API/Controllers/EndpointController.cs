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
        [Tags("General")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [Produces("text/plain")]
        public IActionResult Get()
        {
            return Program.CreateResult("OK");
        }
    }
}