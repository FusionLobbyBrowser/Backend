using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FLB_API.Controllers.Steam
{
    public class AuthorizationController : Controller
    {
        [HttpGet("~/steam/login"), HttpPost("~/steam/login")]
        public async Task<IActionResult> SignIn()
            => Challenge(new AuthenticationProperties { RedirectUri = "https://fusion.hahoos.dev/" }, "Steam");

        [HttpGet("~/steam/logout"), HttpPost("~/steam/logout")]
        public IActionResult SignOutCurrentUser()
            => SignOut(new AuthenticationProperties { RedirectUri = "https://fusion.hahoos.dev/" },
                CookieAuthenticationDefaults.AuthenticationScheme);

        [Authorize]
        [HttpGet("~/steam/me")]
        public IActionResult GetMe()
        {
            return Ok(User.Claims.ToDictionary(x => x.Type, x => x.Value));
        }
    }
}