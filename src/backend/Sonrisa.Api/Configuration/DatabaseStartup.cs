using Microsoft.EntityFrameworkCore;
using Sonrisa.Infrastructure.Persistence;

namespace Sonrisa.Api.Configuration;

public static class DatabaseStartup
{
    /// <summary>Applies pending EF Core migrations at startup (CLAUDE.md tech stack).</summary>
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }
}
