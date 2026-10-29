using Sonrisa.Application.Abstractions;

namespace Sonrisa.Application.Registries;

/// <summary>
/// The only place that maps a channel key to its implementation (ADR-2, AC11). Every channel registered in DI is
/// here; a duplicate key fails at startup.
/// </summary>
public sealed class ChannelRegistry
{
    private readonly Dictionary<string, INotificationChannel> _byKey;

    public ChannelRegistry(IEnumerable<INotificationChannel> channels)
    {
        _byKey = RegistryKeys.ToDictionary(channels, c => c.Key, "channel");
    }

    public IReadOnlyCollection<INotificationChannel> All => _byKey.Values;

    public INotificationChannel? Find(string key) => _byKey.GetValueOrDefault(key);

    /// <summary>
    /// The masked config of a stored destination. A destination of a channel that is no longer registered shows an
    /// empty object, so nothing unmasked can leak.
    /// </summary>
    public string MaskedConfig(string channelKey, string configJson) =>
        Find(channelKey)?.MaskConfig(configJson) ?? "{}";

    public string DisplayNameOf(string channelKey) => Find(channelKey)?.DisplayName ?? channelKey;
}
