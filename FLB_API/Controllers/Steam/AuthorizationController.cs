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
        [HttpGet("login", Name = "SteamLogin"), HttpPost("login", Name = "SteamLogin")]
        public async Task<IActionResult> SignIn([FromQuery(Name = "redirectURL")] string redirectUrl = "")
            => Challenge(new AuthenticationProperties { RedirectUri = (string.IsNullOrWhiteSpace(redirectUrl) ? "https://fusion.hahoos.dev/" : redirectUrl), IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddYears(1) }, "Steam");

        [HttpGet("logout", Name = "SteamLogout"), HttpPost("logout", Name = "SteamLogout")]
        public IActionResult SignOutCurrentUser([FromQuery(Name = "redirectURL")] string redirectUrl = "")
            => SignOut(new AuthenticationProperties { RedirectUri = (string.IsNullOrWhiteSpace(redirectUrl) ? "https://fusion.hahoos.dev/" : redirectUrl) },
                CookieAuthenticationDefaults.AuthenticationScheme);

        [Authorize]
        [HttpGet("me", Name = "GetSteamMe")]
        public async Task<IActionResult> GetMe()
        {
            var profile = await User.GetSteamProfile();
            if (profile?.Profile == null)
                return Program.CreateResult("Steam API returned no profile for such ID!", 400);

            return Ok(profile.ProfileJson);
        }
    }

    public static class SteamHelper
    {
        extension(ClaimsPrincipal user)
        {
            public long GetSteamId()
            {
                if (user?.Identity?.IsAuthenticated != true)
                    return -1;

                var link = user?.Claims?.FirstOrDefault(x => x.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier");
                if (link == null)
                    return -1;

                return long.Parse(link.Value.Replace("https://steamcommunity.com/openid/id/", string.Empty));
            }

            public async Task<ProfileCache?> GetSteamProfile()
            {
                var id = user.GetSteamId();
                if (id == -1)
                    return null;
                return await ProfileController.GetProfile((ulong)id);
            }
        }
    }
}