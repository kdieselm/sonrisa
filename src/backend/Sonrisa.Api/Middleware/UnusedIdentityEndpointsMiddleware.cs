namespace Sonrisa.Api.Middleware;

/// <summary>
/// Answers 404 for every path under <c>/api/auth</c> except the endpoints in scope (spec §2.8, T55):
/// email confirmation, password reset, 2FA and account info are not available.
/// </summary>
/// <remarks>
/// Runs first in the pipeline, before authentication, model binding and endpoint filters. An endpoint filter
/// (the spec's first idea) runs only after the request body binds, so a malformed body got 400 and showed that
/// the endpoint exists, and authorization answered 401 on <c>/manage/*</c> before the filter ran.
/// </remarks>
public sealed class UnusedIdentityEndpointsMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> AllowedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/register", "/login", "/refresh", "/logout",
    };

    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase, out var rest)
            && !AllowedPaths.Contains(rest.Value ?? string.Empty))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        return next(context);
    }
}
