namespace Sonrisa.Domain.Categories.Market;

/// <summary>
/// Data of a market event (spec §2.3): one asset moved across a 0.5-point grid line between two polls. Changes are
/// the provider's own percentage (spec §2.4); <paramref name="Baseline"/> says what that percentage is relative to.
/// </summary>
public sealed record MarketData(
    string Asset,
    string AssetName,
    double PreviousChangePercent,
    double CurrentChangePercent,
    string Baseline);
