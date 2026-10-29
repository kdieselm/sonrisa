using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Sonrisa.Application.Services;
using Sonrisa.Domain.Entities;
using Sonrisa.Infrastructure.Persistence;

namespace Sonrisa.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(UserManager<AppUser> userManager, AuditService audit,
    ILogger<AuthController> logger) : ControllerBase
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
            await TryAuditLogoutAsync(user.Id, AuditOutcome.Failure);
            return Problem("Logout failed. Please try again.", statusCode: StatusCodes.Status500InternalServerError);
        }

        await TryAuditLogoutAsync(user.Id, AuditOutcome.Success);
        return NoContent();
    }

    /// <summary>
    /// The stamp is already updated at this point, so a failed audit write must not turn a done logout into an
    /// error; it is logged instead. Not tied to the request token, so a disconnecting client does not cancel it.
    /// </summary>
    private async Task TryAuditLogoutAsync(string userId, AuditOutcome outcome)
    {
        try
        {
            await audit.RecordAsync(userId, AuditActions.Logout, AuditTargets.User, userId, outcome,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not write the logout audit entry for user {UserId}.", userId);
        }
    }
}
