using Sonrisa.Application.Abstractions;
using Sonrisa.Domain.Categories;
using Sonrisa.Domain.Categories.Market;

namespace Sonrisa.Infrastructure.Sources.Market;

/// <summary>One observed change of an asset, as a provider reports it (spec §2.4).</summary>
public sealed record AssetReading(MarketAsset Asset, double ChangePercent);

/// <summary>
/// The part every market source shares (spec §2.4): remember the last change per asset and emit one event when it
/// crossed a 0.5-point grid line since the previous poll. No event on the first poll after a start.
/// </summary>
public sealed class MarketMovements(IObservationStore store, TimeProvider time)
{
    public IReadOnlyList<IncomingEvent> Record(IEnumerable<AssetReading> readings, string baseline,
        Func<MarketAsset, string?> url)
    {
        var now = time.GetUtcNow();
        var events = new List<IncomingEvent>();
        foreach (var (asset, change) in readings.Where(r => double.IsFinite(r.ChangePercent)))
        {
            if (store.Update(asset.Key, change) is not { } previous || !MarketLogic.CrossedGrid(previous, change))
            {
                continue;
            }

            var data = new MarketData(asset.Key, asset.DisplayName, previous, change, baseline);
            events.Add(new IncomingEvent($"{asset.Key}:{now.ToUnixTimeSeconds()}", MarketCategory.TitleOf(data), null,
                url(asset), now, MarketCategory.Serialize(data)));
        }

        return events;
    }
}
