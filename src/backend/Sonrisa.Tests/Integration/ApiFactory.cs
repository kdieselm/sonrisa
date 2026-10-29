using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Sonrisa.Tests.Integration;

/// <summary>Runs the real API (startup migration and admin seeding included) against the test container.</summary>
public sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@example.test";
    public const string AdminPassword = "Admin-Password1!";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
    }
}
