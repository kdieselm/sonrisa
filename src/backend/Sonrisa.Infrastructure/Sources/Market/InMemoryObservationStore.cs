using System.Collections.Concurrent;
using Sonrisa.Domain.Categories;

namespace Sonrisa.Infrastructure.Sources.Market;

/// <summary>The last observed change per asset, in memory (ADR-5). Asset keys are compared without case.</summary>
public sealed class InMemoryObservationStore : IObservationStore
{
    private readonly ConcurrentDictionary<string, double> _latest = new(StringComparer.OrdinalIgnoreCase);

    public double? Latest(string asset) => _latest.TryGetValue(asset, out var value) ? value : null;

    public double? Update(string asset, double changePercent)
    {
        double? previous = null;
        _latest.AddOrUpdate(asset, changePercent, (_, old) =>
        {
            previous = old;
            return changePercent;
        });
        return previous;
    }
}
