using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sonrisa.Application.Abstractions;
using Sonrisa.Infrastructure.Channels.Slack.Mock;
using Sonrisa.Infrastructure.Persistence.Repositories;

namespace Sonrisa.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services,
        Func<IServiceProvider, string> connectionString)
    {
        services.AddDbContext<AppDbContext>((sp, options) => options.UseNpgsql(connectionString(sp)));

        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IAlertRepository, AlertRepository>();
        services.AddScoped<IDestinationRepository, DestinationRepository>();
        services.AddScoped<IDeliveryRepository, DeliveryRepository>();
        services.AddScoped<ITransactionRunner, EfTransactionRunner>();
        services.AddScoped<ISlackMockStore, SlackMockStore>();
        return services;
    }
}
