using Sonrisa.Api.HostedServices;
using Sonrisa.Application.Configuration;
using Sonrisa.Application.Services;

namespace Sonrisa.Api.Configuration;

/// <summary>Registers the delivery worker and its settings (spec §2.7, architecture §3.2).</summary>
public static class DeliveryRegistration
{
    public static IServiceCollection AddDelivery(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(DeliveryOptions.Section).Get<DeliveryOptions>() ?? new DeliveryOptions();
        if (options.MaxAttempts < 1 || options.BatchSize < 1 || options.PollInterval <= TimeSpan.Zero
            || options.SendTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Delivery: MaxAttempts and BatchSize must be at least 1; "
                                                + "PollInterval and SendTimeout must be positive.");
        }

        services.AddSingleton(options);
        services.AddScoped<DeliveryProcessor>();
        services.AddSingleton<DeliveryWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<DeliveryWorker>());
        return services;
    }
}
