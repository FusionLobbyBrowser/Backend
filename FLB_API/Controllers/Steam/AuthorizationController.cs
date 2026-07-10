using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FLB_API.Controllers.Steam
{
    [ApiController]
    [Route("steam")]
    public class AuthorizationController : ControllerBase
    {
        [HttpGet("login"), HttpPost("login")]
        public async Task<IActionResult> SignIn([FromQuery(Name = "redirectURL")] string redirectURL = "")
            => Challenge(new AuthenticationProperties { RedirectUri = (string.IsNullOrWhiteSpace(redirectURL) ? "https://fusion.hahoos.dev/" : redirectURL), IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddYears(1) }, "Steam");

        [HttpGet("logout"), HttpPost("logout")]
        public IActionResult SignOutCurrentUser([FromQuery(Name = "redirectURL")] string redirectURL = "")
            => SignOut(new AuthenticationProperties { RedirectUri = (string.IsNullOrWhiteSpace(redirectURL) ? "https://fusion.hahoos.dev/" : redirectURL) },
                CookieAuthenticationDefaults.AuthenticationScheme);

        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> GetMe()
        {
            var profile = await User.GetSteamProfile();
            if (profile?.Profile == null)
                return Program.CreateResult("Steam API returned no profile for such ID!", 400);

            return Ok(profile.ProfileJSON);
        }
    }

    public static class SteamHelper
    {
        public static long GetSteamID(this ClaimsPrincipal user)
        {
            if (user?.Identity?.IsAuthenticated != true)
                return -1;

            var link = user?.Claims?.FirstOrDefault(x => x.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier");
            if (link == null)
                return -1;

            return long.Parse(link.Value.Replace("https://steamcommunity.com/openid/id/", string.Empty));
        }

        public static async Task<ProfileCache?> GetSteamProfile(this ClaimsPrincipal user)
        {
            var id = user.GetSteamID();
            if (id == -1)
                return null;
            return await ProfileController.GetProfile((ulong)id);
        }
    }
}