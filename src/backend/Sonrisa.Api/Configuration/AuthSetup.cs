using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Sonrisa.Infrastructure.Persistence;

namespace Sonrisa.Api.Configuration;

public static class AuthSetup
{
    /// <summary>Access-token lifetime (spec §2.8, D9). The framework default is 1 hour.</summary>
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(10);

    public static IServiceCollection AddAuth(this IServiceCollection services)
    {
        services.AddAuthorization();

        services.AddIdentityApiEndpoints<AppUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        services.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, options =>
        {
            options.BearerTokenExpiration = AccessTokenLifetime;
        });

        return services;
    }

    /// <summary>
    /// Maps the Identity endpoints under <c>/api/auth</c>. The unused ones are blocked by
    /// <see cref="Middleware.UnusedIdentityEndpointsMiddleware"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/auth").MapIdentityApi<AppUser>();
        return app;
    }
}
