using Sonrisa.Application.Abstractions;

namespace Sonrisa.Application.Registries;

/// <summary>
/// The only place that maps a source key to its adapter (ADR-2, AC11). Fails at startup on a duplicate key, on a
/// source whose category is not registered, and on the reserved key of simulated events.
/// </summary>
public sealed class SourceRegistry
{
    /// <summary>Source key of events created by the admin "simulate event" form (spec §2.3). Not a polled source.</summary>
    public const string SimulatedKey = "simulated";

    public const string SimulatedDisplayName = "Simulated (admin)";

    private readonly Dictionary<string, IEventSource> _byKey;

    public SourceRegistry(IEnumerable<IEventSource> sources, CategoryRegistry categories)
    {
        _byKey = RegistryKeys.ToDictionary(sources, s => s.Key, "source");

        if (_byKey.ContainsKey(SimulatedKey))
        {
            throw new InvalidOperationException($"The source key '{SimulatedKey}' is reserved for simulated events.");
        }

        foreach (var source in _byKey.Values)
        {
            if (categories.Find(source.CategoryKey) is null)
            {
                throw new InvalidOperationException(
                    $"Source '{source.Key}' uses category '{source.CategoryKey}', which is not registered.");
            }

            if (source.Interval <= TimeSpan.Zero)
            {
                throw new InvalidOperationException($"Source '{source.Key}' needs a positive poll interval.");
            }
        }
    }

    public IReadOnlyCollection<IEventSource> All => _byKey.Values;

    public IEventSource? Find(string key) => _byKey.GetValueOrDefault(key);

    /// <summary>Label of a source key, including the reserved simulated one; the key itself if it is unknown.</summary>
    public string DisplayNameOf(string key) =>
        key == SimulatedKey ? SimulatedDisplayName : Find(key)?.DisplayName ?? key;
}
