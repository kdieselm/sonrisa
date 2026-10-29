using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Sonrisa.Application.Services;
using Sonrisa.Contracts.Dtos.Alerts;
using Sonrisa.Contracts.Dtos.Destinations;
using Sonrisa.Domain.Categories.Disaster;
using Sonrisa.Infrastructure.Channels.Email;
using Sonrisa.Infrastructure.Channels.Slack;
using Sonrisa.Infrastructure.Persistence;

namespace Sonrisa.Api.Configuration;

/// <summary>
/// The demo user for the end-to-end run (AC1, D27): a normal account with an email destination, a Slack destination
/// and one disaster alert, so a reviewer only has to log in and let the admin simulate an event. Created only when
/// <c>Seed:DemoEmail</c> is set (Compose: <c>SEED_DEMO_*</c>). It goes through the same services as the user API, so
/// the data is validated like anything a user enters.
/// </summary>
public static class DemoSeeding
{
    public const string DemoAlertName = "Demo: earthquakes M 5 and above";

    /// <summary>
    /// Creates the demo user once. Safe to run on every start: an existing account is left alone, so nothing the user
    /// changed is overwritten. <c>Seed:DemoSlackWebhookUrl</c> (from <c>SLACK_WEBHOOK_URL</c>) makes the Slack
    /// destination a real webhook; empty = the local mock receiver (spec §2.6).
    /// </summary>
    public static async Task SeedDemoUserAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DemoSeeding));
        var options = app.Configuration.GetSection(SeedOptions.Section).Get<SeedOptions>() ?? new SeedOptions();

        if (string.IsNullOrWhiteSpace(options.DemoEmail))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.DemoPassword))
        {
            throw new InvalidOperationException("Seed:DemoEmail is set but Seed:DemoPassword is not.");
        }

        var users = services.GetRequiredService<UserManager<AppUser>>();
        if (await users.FindByEmailAsync(options.DemoEmail) is not null)
        {
            return;
        }

        var user = new AppUser { UserName = options.DemoEmail, Email = options.DemoEmail };
        var created = await users.CreateAsync(user, options.DemoPassword);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not create the demo user: {string.Join(", ", created.Errors.Select(e => e.Code))}");
        }

        var destinations = services.GetRequiredService<DestinationService>();
        var alerts = services.GetRequiredService<AlertService>();
        var ct = CancellationToken.None;

        var email = await destinations.CreateAsync(user.Id,
            new CreateDestinationRequest(EmailChannel.ChannelKey, "Demo email", Json("{}")), ct);
        var slackConfig = string.IsNullOrWhiteSpace(options.DemoSlackWebhookUrl)
            ? "{}"
            : JsonSerializer.Serialize(new { webhookUrl = options.DemoSlackWebhookUrl.Trim() });
        var slack = await destinations.CreateAsync(user.Id,
            new CreateDestinationRequest(SlackChannel.ChannelKey, "Demo Slack", Json(slackConfig)), ct);
        if (email.Value is null || slack.Value is null)
        {
            throw new InvalidOperationException("Could not create the demo destinations: "
                                                + Describe(email.Errors) + Describe(slack.Errors));
        }

        var alert = await alerts.CreateAsync(user.Id,
            new CreateAlertRequest(DemoAlertName, DisasterCategory.CategoryKey, Json("{\"minMagnitude\":5}"),
                [email.Value.Id, slack.Value.Id]), ct);
        if (alert.Value is null)
        {
            throw new InvalidOperationException("Could not create the demo alert: " + Describe(alert.Errors));
        }

        logger.LogInformation("Demo user {Email} created with 2 destinations and 1 alert.", options.DemoEmail);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string Describe(IReadOnlyDictionary<string, string[]>? errors) =>
        errors is null ? string.Empty : string.Join("; ", errors.Select(e => $"{e.Key}: {string.Join(" ", e.Value)}"));
}
