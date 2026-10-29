namespace Sonrisa.Tests.Unit;

/// <summary>A settable clock; enough for the unit tests without a testing package.</summary>
internal sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
