using Sonrisa.Application.Abstractions;
using Sonrisa.Domain.Categories.Market;
using Sonrisa.Infrastructure.Configuration;

namespace Sonrisa.Infrastructure.Sources.Market;

/// <summary>
/// Stocks without a Finnhub key (spec §2.5): a random walk of the change in percent per asset, its events are
/// marked simulated. The walk is pulled back toward zero so values stay realistic.
/// </summary>
public sealed class MockStocksSource(
    MarketMovements movements,
    MockStocksOptions options,
    IReadOnlyList<MarketAsset> assets,
    Random random) : IEventSource
{
    public const string SourceKey = "mock-stocks";
    public const string Baseline = "simulated change";

    private readonly Dictionary<string, double> _changes = [];

    public string Key => SourceKey;
    public string DisplayName => "Mock stocks (simulated)";
    public string CategoryKey => MarketCategory.CategoryKey;
    public TimeSpan Interval => options.Interval;
    public bool ProducesSimulatedEvents => true;

    public Task<IReadOnlyList<IncomingEvent>> FetchAsync(CancellationToken ct)
    {
        var readings = assets.Select(asset => new AssetReading(asset, Next(asset.Key))).ToList();
        return Task.FromResult(movements.Record(readings, Baseline, _ => null));
    }

    /// <summary>Public so the walk can be tested without waiting.</summary>
    public double Next(string asset)
    {
        var current = _changes.GetValueOrDefault(asset);
        var next = Math.Round((current * 0.97) + ((random.NextDouble() * 2 - 1) * options.MaxStepPercent), 2);
        _changes[asset] = next;
        return next;
    }
}
