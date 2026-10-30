namespace Sonrisa.Domain.Categories.Market;

/// <summary>
/// The pure market rules of spec §2.4, for one direction in "up" terms (the caller mirrors the value for "down").
/// Fire on a new crossing, escalate by steps, re-arm one point below the threshold (hysteresis).
/// </summary>
public static class MarketLogic
{
    /// <summary>Events are emitted only on crossings of this grid (spec §2.4); thresholds and steps are multiples of it.</summary>
    public const double Grid = 0.5;

    /// <summary>The value must fall this far below the threshold before the alert is armed again.</summary>
    public const double RearmMargin = 1;

    private const double Epsilon = 1e-9;

    public static bool CrossedGrid(double previous, double current) => Cell(previous) != Cell(current);

    /// <summary>
    /// Grid cell of a value, symmetric around zero: a value exactly on a line belongs to the cell away from zero, so
    /// -5.0 counts as having reached -5 just as 5.0 counts as having reached 5 (S5 review finding 1).
    /// </summary>
    private static double Cell(double value) =>
        value >= 0 ? Math.Floor(value / Grid + Epsilon) : -Math.Floor(-value / Grid + Epsilon);

    public static bool IsMultipleOfGrid(double value) =>
        Math.Abs(value / Grid - Math.Round(value / Grid)) < Epsilon;

    /// <summary>
    /// Start state when an alert becomes active (spec §2.4): disarmed if the latest value is already past the
    /// threshold, so only a new crossing notifies; armed otherwise or when nothing was observed yet (callers that have no
    /// observation keep the start pending instead, see <c>MarketCategory.InitialState</c>).
    /// </summary>
    public static MarketSide Start(double? latest, double threshold, double? step) =>
        latest is { } value && value >= threshold - Epsilon
            ? new MarketSide(false, ReachedLevel(value, threshold, step))
            : new MarketSide(true, null);

    public static (bool Fired, MarketSide Side) Step(MarketSide side, double value, double threshold, double? step)
    {
        if (value < threshold - RearmMargin - Epsilon)
        {
            return (false, new MarketSide(true, null));
        }

        if (side.Armed)
        {
            return value >= threshold - Epsilon
                ? (true, new MarketSide(false, ReachedLevel(value, threshold, step)))
                : (false, side);
        }

        if (step is { } s && side.LastLevel is { } last && value >= last + s - Epsilon)
        {
            return (true, new MarketSide(false, ReachedLevel(value, threshold, step)));
        }

        return (false, side);
    }

    /// <summary>The highest level <c>threshold + k·step</c> (k ≥ 0) that <paramref name="value"/> has reached.</summary>
    public static double ReachedLevel(double value, double threshold, double? step) =>
        step is { } s ? threshold + Math.Floor((value - threshold) / s + Epsilon) * s : threshold;
}
