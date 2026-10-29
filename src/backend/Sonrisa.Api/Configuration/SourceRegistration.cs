using Sonrisa.Api.HostedServices;
using Sonrisa.Application.Abstractions;
using Sonrisa.Application.Registries;
using Sonrisa.Application.Services;
using Sonrisa.Domain.Categories;
using Sonrisa.Domain.Categories.Disaster;
using Sonrisa.Domain.Categories.Market;
using Sonrisa.Domain.Categories.News;
using Sonrisa.Infrastructure.Configuration;
using Sonrisa.Infrastructure.Sources;
using Sonrisa.Infrastructure.Sources.Market;
using Sonrisa.Infrastructure.Sources.Resilience;
using Sonrisa.Infrastructure.Sources.Rss;
using Sonrisa.Infrastructure.Sources.Usgs;

namespace Sonrisa.Api.Configuration;

/// <summary>
/// The one place where event categories and sources are registered (architecture §3.1, T85). Adding a category or a
/// source adds one line here; no other code knows the list (see <c>docs/how-to/</c>).
/// </summary>
public static class SourceRegistration
{
    public static IServiceCollection AddEvents(this IServiceCollection services, IConfiguration configuration)
    {
        // The source list is read here, at registration. PollingEnabled is read when the poller starts (IOptions), so
        // a test host can switch polling off with a setting.
        var options = configuration.GetSection(SourcesOptions.Section).Get<SourcesOptions>() ?? new SourcesOptions();
        services.AddSingleton(options);
        services.Configure<SourcesOptions>(configuration.GetSection(SourcesOptions.Section));

        // Categories (spec §2.2).
        services.AddSingleton<IEventCategory, DisasterCategory>();
        services.AddSingleton<IEventCategory, NewsCategory>();
        var assets = options.Market.Assets.Select(a => new MarketAsset(a.Key, a.DisplayName, a.Kind)).ToList();
        services.AddSingleton(new MarketAssetCatalog(assets));
        services.AddSingleton<IEventCategory, MarketCategory>();

        // Sources (spec §2.5). The RSS feed list is configuration (Sources:Rss).
        services.AddSingleton<SourceHttpClient>();
        if (options.Usgs.Enabled)
        {
            services.AddSingleton<IEventSource>(sp =>
                new UsgsSource(sp.GetRequiredService<SourceHttpClient>(), options.Usgs));
        }

        foreach (var feed in options.Rss)
        {
            services.AddSingleton<IEventSource>(sp => new RssSource(sp.GetRequiredService<SourceHttpClient>(), feed));
        }

        // News: the Guardian first, the BBC only as a fallback (D26). Sources:RssFailover.
        foreach (var news in options.RssFailover)
        {
            services.AddSingleton<IEventSource>(sp =>
            {
                var http = sp.GetRequiredService<SourceHttpClient>();
                var time = sp.GetRequiredService<TimeProvider>();
                return new FailoverSource(news.Key, news.DisplayName, news.Interval,
                    new RssSource(http, news.FeedOptions(news.Primary)),
                    new RssSource(http, news.FeedOptions(news.Fallback)),
                    new CircuitBreaker(news.FailuresToOpen, news.OpenDuration, time),
                    news.Attempts, news.EffectiveRetryDelays, time,
                    sp.GetRequiredService<ILoggerFactory>().CreateLogger<FailoverSource>());
            });
        }

        // Market (spec §2.4, §2.5): crypto from CoinGecko; stocks from Finnhub if a key is set, else the mock.
        services.AddSingleton<IObservationStore, InMemoryObservationStore>();
        services.AddSingleton<MarketMovements>();
        var crypto = assets.Where(a => a.Kind == MarketAsset.Crypto).ToList();
        var stocks = assets.Where(a => a.Kind == MarketAsset.Stock).ToList();
        if (options.Market.CoinGecko.Enabled && crypto.Count > 0)
        {
            services.AddSingleton<IEventSource>(sp => new CoinGeckoSource(sp.GetRequiredService<SourceHttpClient>(),
                sp.GetRequiredService<MarketMovements>(), options.Market.CoinGecko, crypto));
        }

        if (stocks.Count > 0 && !string.IsNullOrWhiteSpace(options.Market.Finnhub.ApiKey))
        {
            services.AddSingleton<IEventSource>(sp => new FinnhubSource(sp.GetRequiredService<SourceHttpClient>(),
                sp.GetRequiredService<MarketMovements>(), options.Market.Finnhub, stocks));
        }
        else if (stocks.Count > 0 && options.Market.MockStocks.Enabled)
        {
            services.AddSingleton<IEventSource>(sp => new MockStocksSource(sp.GetRequiredService<MarketMovements>(),
                options.Market.MockStocks, stocks, Random.Shared));
        }

        services.AddSingleton<CategoryRegistry>();
        services.AddSingleton<SourceRegistry>();
        services.AddScoped<EventIngestor>();
        services.AddScoped<EventAdminService>();
        services.AddHostedService<SourcePoller>();
        return services;
    }

    /// <summary>Builds both registries at startup, so a duplicate key or an unknown category stops the app at once.</summary>
    public static void ValidateEventRegistries(this WebApplication app)
    {
        app.Services.GetRequiredService<CategoryRegistry>();
        app.Services.GetRequiredService<SourceRegistry>();
    }
}
