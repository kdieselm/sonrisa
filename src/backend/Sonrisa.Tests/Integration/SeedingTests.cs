using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sonrisa.Infrastructure.Persistence;

namespace Sonrisa.Tests.Integration;

/// <summary>Seeded admin (spec §2.8, T51): created once, in the Admin role, never by promoting a normal account.</summary>
[Collection(PostgresCollection.Name)]
public sealed class SeedingTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Starting_twice_leaves_one_admin_in_the_admin_role()
    {
        await using (var first = new ApiFactory(postgres.ConnectionString))
        {
            first.CreateClient().Dispose(); // starts the host: migrate + seed
        }

        await using var second = new ApiFactory(postgres.ConnectionString);
        second.CreateClient().Dispose();

        await using var scope = second.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var admins = await db.Users.Where(u => u.Email == ApiFactory.AdminEmail).ToListAsync(TestApi.Ct);
        var admin = Assert.Single(admins);
        Assert.True(await users.IsInRoleAsync(admin, "Admin"));
        Assert.Single(await db.Roles.Where(r => r.Name == "Admin").ToListAsync(TestApi.Ct));
    }

    [Fact]
    public async Task Seed_email_of_an_existing_normal_account_is_not_promoted_and_stops_startup()
    {
        // A normal user registers first; then the API is started with that email as seed admin.
        var email = TestApi.NewEmail();
        await using (var api = new ApiFactory(postgres.ConnectionString))
        {
            using var client = api.CreateClient();
            await client.RegisterAsync(email);
        }

        await using var hijack = new ApiFactory(postgres.ConnectionString)
            .WithWebHostBuilder(b => b.UseSetting("Seed:AdminEmail", email));
        var error = Assert.Throws<InvalidOperationException>(() => hijack.CreateClient());
        Assert.Contains("not an admin", error.Message);

        await using var check = new ApiFactory(postgres.ConnectionString);
        await using var scope = check.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByEmailAsync(email);
        Assert.False(await users.IsInRoleAsync(user!, "Admin"));
    }

    [Fact]
    public async Task Missing_seed_admin_config_stops_startup()
    {
        await using var api = new ApiFactory(postgres.ConnectionString)
            .WithWebHostBuilder(b => b.UseSetting("Seed:AdminPassword", ""));
        var error = Assert.Throws<InvalidOperationException>(() => api.CreateClient());
        Assert.Contains("not configured", error.Message);
    }
}
