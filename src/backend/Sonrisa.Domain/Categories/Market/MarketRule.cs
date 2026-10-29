namespace Sonrisa.Domain.Categories.Market;

/// <summary>
/// Rule of a market alert (spec §2.2): the asset, the direction (<c>up</c>, <c>down</c>, <c>either</c>), a threshold in
/// percent (at least 1, a multiple of 0.5) and an optional step for repeat notifications (§2.4).
/// </summary>
public sealed record MarketRule(string Asset, string Direction, double ThresholdPercent, double? StepPercent)
{
    public const string Up = "up";
    public const string Down = "down";
    public const string Either = "either";

    public const double MinThreshold = 1;
    public const double MaxPercent = 100;
}

/// <summary>Armed state and last notified level of one direction (spec §2.4).</summary>
public sealed record MarketSide(bool Armed, double? LastLevel);

/// <summary>The state of a market alert, stored in <c>AlertStates.State</c>. A side that is null is armed.</summary>
public sealed record MarketState(MarketSide? Up, MarketSide? Down);
