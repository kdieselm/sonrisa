namespace Sonrisa.Domain.Categories.Market;

/// <summary>One tradable asset an alert can watch (spec §2.5). <see cref="Kind"/> tells the sources which one reads it.</summary>
public sealed record MarketAsset(string Key, string DisplayName, string Kind)
{
    public const string Crypto = "crypto";
    public const string Stock = "stock";
}

/// <summary>The configured assets (spec §2.5: a list in configuration, not in code).</summary>
public sealed class MarketAssetCatalog(IEnumerable<MarketAsset> assets)
{
    public IReadOnlyList<MarketAsset> All { get; } = assets.ToList();

    public MarketAsset? Find(string? key) =>
        All.FirstOrDefault(a => string.Equals(a.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase));
}
