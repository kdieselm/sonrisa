using Sonrisa.Api.Configuration;
using Sonrisa.Api.Middleware;
using Sonrisa.Application.Services;
using Sonrisa.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// The connection string is read when the DbContext is first resolved, not here, so test hosts can override it.
builder.Services.AddPersistence(sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is not configured."));
builder.Services.AddAuth();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<AdminQueryService>();
builder.Services.AddEvents(builder.Configuration);
builder.Services.AddChannels(builder.Configuration);
builder.Services.AddDelivery(builder.Configuration);
builder.Services.AddScoped<DestinationService>();
builder.Services.AddScoped<AlertService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddControllers();

var app = builder.Build();

app.ValidateEventRegistries();
app.ValidateChannelRegistry();
await app.MigrateDatabaseAsync();
await app.SeedAdminAsync();
await app.SeedDemoUserAsync();

app.UseMiddleware<UnusedIdentityEndpointsMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapAuth();

app.Run();

/// <summary>Entry point, public for <c>WebApplicationFactory</c> in the tests.</summary>
public partial class Program;
