using Sonrisa.Application.Abstractions;
using Sonrisa.Application.Registries;
using Sonrisa.Infrastructure.Channels.Email;
using Sonrisa.Infrastructure.Channels.Slack;
using Sonrisa.Infrastructure.Configuration;

namespace Sonrisa.Api.Configuration;

/// <summary>
/// The one place where notification channels are registered (spec §2.11, T85). Adding a channel adds one line here;
/// no other code knows the list (see <c>docs/how-to/add-a-channel.md</c>).
/// </summary>
public static class ChannelRegistration
{
    public static IServiceCollection AddChannels(this IServiceCollection services, IConfiguration configuration)
    {
        var slack = configuration.GetSection(SlackOptions.Section).Get<SlackOptions>() ?? new SlackOptions();
        services.AddSingleton(slack);

        // Channels (spec §2.6).
        services.AddSingleton<INotificationChannel, EmailChannel>();
        services.AddSingleton<INotificationChannel, SlackChannel>();

        services.AddSingleton<ChannelRegistry>();
        return services;
    }

    /// <summary>Builds the registry at startup, so a duplicate key stops the app at once.</summary>
    public static void ValidateChannelRegistry(this WebApplication app) =>
        app.Services.GetRequiredService<ChannelRegistry>();
}
