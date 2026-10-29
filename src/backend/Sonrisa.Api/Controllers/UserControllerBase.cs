using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sonrisa.Application;
using Sonrisa.Application.Services;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Api.Controllers;

/// <summary>
/// Base of the user API (spec §2.8): authorized, and every call works on the caller's own data. Another user's ID is
/// answered with 404, never 403, so IDs are not confirmed.
/// </summary>
[ApiController]
[Authorize]
public abstract class UserControllerBase(AuditService audit, ILogger logger) : ControllerBase
{
    /// <summary>The caller's user ID from the bearer token (always present behind <c>[Authorize]</c>).</summary>
    protected string CallerId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Authenticated request without a user ID.");

    /// <summary>
    /// Writes one audit entry after the change is committed (spec §2.10). A failed write is logged, not turned into an
    /// error: the change already happened, and a retry by the client would repeat it. Only IDs, never config.
    /// </summary>
    protected async Task AuditAsync(string action, string targetType, long targetId)
    {
        try
        {
            await audit.RecordAsync(CallerId, action, targetType,
                targetId.ToString(CultureInfo.InvariantCulture), AuditOutcome.Success, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not write the audit entry {Action} for {TargetType} {TargetId}.", action,
                targetType, targetId);
        }
    }

    /// <summary>404, 400 with field errors, or the value.</summary>
    protected ActionResult? ErrorFor<T>(ServiceResult<T> result) where T : class =>
        result.IsNotFound
            ? NotFound()
            : result.Errors is { } errors
                ? ValidationProblem(new ValidationProblemDetails(errors.ToDictionary(e => e.Key, e => e.Value)))
                : null;
}
