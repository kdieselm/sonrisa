using Microsoft.AspNetCore.Identity.Data;
using Sonrisa.Application.Abstractions;
using Sonrisa.Application.Services;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Api.Filters;

/// <summary>
/// Writes audit entries for <c>/register</c> and <c>/login</c> of <c>MapIdentityApi</c> from their result
/// (spec §2.8, §2.10). Other Identity endpoints pass through unchanged.
/// </summary>
/// <remarks>
/// <c>/register</c> returns <c>Results&lt;Ok, ValidationProblem&gt;</c>; a bearer <c>/login</c> returns
/// <c>Empty</c> on success (the token is already written) and a 401 <c>Problem</c> on any failure
/// (release/10.0 source, log 17:21). So a status code of 400 or more means failure.
/// The email and password are never stored. The target is the user ID found by email (null if none); the actor is
/// that user only on success, because a failed attempt may come from someone else.
/// </remarks>
public sealed class IdentityAuditFilter(AuditService audit, IUserDirectory users, ILogger<IdentityAuditFilter> logger)
    : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        string? action = null;
        string? email = null;
        foreach (var argument in context.Arguments)
        {
            switch (argument)
            {
                case RegisterRequest register:
                    (action, email) = (AuditActions.Register, register.Email);
                    break;
                case LoginRequest login:
                    (action, email) = (AuditActions.Login, login.Email);
                    break;
            }
        }

        var result = await next(context);

        if (action is null)
        {
            return result;
        }

        try
        {
            var outcome = IsFailure(result) ? AuditOutcome.Failure : AuditOutcome.Success;
            // Not the request's token: a client that disconnects after the token was written must not lose the entry.
            var ct = CancellationToken.None;
            var userId = string.IsNullOrWhiteSpace(email) ? null : await users.FindIdByEmailAsync(email, ct);
            var actor = outcome == AuditOutcome.Success ? userId : null;
            await audit.RecordAsync(actor, action, AuditTargets.User, userId, outcome, ct);
        }
        catch (Exception ex)
        {
            // A successful bearer login has already written its token to the response, so throwing here would
            // break a completed request. The missing entry is logged instead.
            logger.LogError(ex, "Could not write the audit entry for {Action}.", action);
        }

        return result;
    }

    private static bool IsFailure(object? result)
    {
        while (result is INestedHttpResult nested)
        {
            result = nested.Result;
        }

        return result is IStatusCodeHttpResult { StatusCode: >= 400 };
    }
}
