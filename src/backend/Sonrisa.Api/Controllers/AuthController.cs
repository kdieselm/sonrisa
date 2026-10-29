using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Sonrisa.Infrastructure.Persistence;

namespace Sonrisa.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(UserManager<AppUser> userManager, ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>
    /// Invalidates every refresh token of the caller by updating the security stamp, so <c>/api/auth/refresh</c>
    /// returns 401 afterwards (D5, spec §2.8). The access token stays valid until it expires (known limit, spec §7).
    /// </summary>
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var result = await userManager.UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
        {
            // Without a new stamp the old refresh token would still work, so the client must not see success.
            logger.LogError("Logout failed to update the security stamp for user {UserId}: {Errors}",
                user.Id, string.Join(", ", result.Errors.Select(e => e.Code)));
            return Problem("Logout failed. Please try again.", statusCode: StatusCodes.Status500InternalServerError);
        }

        return NoContent();
    }
}
