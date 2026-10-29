using Microsoft.AspNetCore.Identity;
using Sonrisa.Infrastructure.Persistence;

namespace Sonrisa.Api.Configuration;

/// <summary>Seed accounts, bound from the <c>Seed</c> section (Compose: <c>SEED_ADMIN_*</c>, <c>SEED_DEMO_*</c>).</summary>
public sealed class SeedOptions
{
    public const string Section = "Seed";

    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }

    /// <summary>Demo user for the end-to-end run (D27, <see cref="DemoSeeding"/>); empty email = none.</summary>
    public string? DemoEmail { get; set; }

    public string? DemoPassword { get; set; }

    /// <summary>A real Slack webhook for the demo user's Slack destination (AC20); empty = the local mock.</summary>
    public string? DemoSlackWebhookUrl { get; set; }
}

public static class AdminSeeding
{
    public const string AdminRole = "Admin";

    /// <summary>
    /// Creates the <c>Admin</c> role and the seeded admin if they do not exist (spec §2.8, T51). Safe to run on
    /// every start. An existing admin's password is not changed. Startup fails if the seed admin is not configured,
    /// or if its email already belongs to a normal account: promoting an account the seeder did not create would
    /// give admin rights to whoever registered that email first.
    /// </summary>
    public static async Task SeedAdminAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AdminSeeding));
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        var users = services.GetRequiredService<UserManager<AppUser>>();
        var options = app.Configuration.GetSection(SeedOptions.Section).Get<SeedOptions>() ?? new SeedOptions();

        if (!await roles.RoleExistsAsync(AdminRole))
        {
            EnsureSucceeded(await roles.CreateAsync(new IdentityRole(AdminRole)), "create the Admin role");
        }

        if (string.IsNullOrWhiteSpace(options.AdminEmail) || string.IsNullOrWhiteSpace(options.AdminPassword))
        {
            throw new InvalidOperationException(
                "The seed admin is not configured: set SEED_ADMIN_EMAIL and SEED_ADMIN_PASSWORD (.env.example).");
        }

        var existing = await users.FindByEmailAsync(options.AdminEmail);
        if (existing is not null)
        {
            if (!await users.IsInRoleAsync(existing, AdminRole))
            {
                throw new InvalidOperationException(
                    $"The seed admin email {options.AdminEmail} belongs to an account that is not an admin. " +
                    "It is not promoted; choose another SEED_ADMIN_EMAIL.");
            }

            return;
        }

        // Same shape as MapIdentityApi's /register: user name = email.
        var admin = new AppUser { UserName = options.AdminEmail, Email = options.AdminEmail };
        EnsureSucceeded(await users.CreateAsync(admin, options.AdminPassword), "create the seed admin");
        EnsureSucceeded(await users.AddToRoleAsync(admin, AdminRole), "add the seed admin to the Admin role");
        logger.LogInformation("Seed admin {Email} created.", options.AdminEmail);
    }

    private static void EnsureSucceeded(IdentityResult result, string what)
    {
        if (!result.Succeeded)
        {
            // Fail the startup: an app without its admin cannot meet the brief's admin view.
            throw new InvalidOperationException(
                $"Could not {what}: {string.Join(", ", result.Errors.Select(e => e.Code))}");
        }
    }
}
