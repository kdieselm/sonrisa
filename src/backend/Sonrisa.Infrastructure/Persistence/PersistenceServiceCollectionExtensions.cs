using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sonrisa.Application.Abstractions;
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
        services.AddScoped<ITransactionRunner, EfTransactionRunner>();
        return services;
    }
}
