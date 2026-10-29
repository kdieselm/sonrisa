using Sonrisa.Domain.Entities;

namespace Sonrisa.Tests.Unit.Domain;

public sealed class EventTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Revise_ignores_json_formatting_and_key_order()
    {
        var e = new Event("disaster", "usgs", "x", "M 5", null, null, T0, T0, false, """{"magnitude":5,"place":"P"}""");

        // jsonb returns the stored value with its own spacing and key order; that is not a revision.
        Assert.False(e.Revise("M 5", null, null, T0, """{ "place": "P", "magnitude": 5 }""", T0.AddMinutes(1)));
        Assert.Equal(T0, e.UpdatedAt);

        Assert.True(e.Revise("M 5", null, null, T0, """{"magnitude":5.4,"place":"P"}""", T0.AddMinutes(2)));
        Assert.Equal(T0.AddMinutes(2), e.UpdatedAt);
        Assert.Equal(T0, e.FirstSeenAt);
    }

    [Fact]
    public void Times_are_kept_at_database_precision_so_an_unchanged_item_is_not_a_revision()
    {
        // 100 ns ticks below a microsecond are lost in timestamptz; found by the AC16 integration test.
        var published = T0.AddTicks(1234567);
        var e = new Event("news", "rss", "x", "Title", null, null, published, T0.AddTicks(3), false, "{}");

        Assert.Equal(0, e.PublishedAt.UtcTicks % 10);
        Assert.Equal(T0, e.FirstSeenAt);
        Assert.False(e.Revise("Title", null, null, published, "{}", T0.AddMinutes(1)));
    }
}
