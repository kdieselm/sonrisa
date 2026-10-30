using System.Text.Json;
using Sonrisa.Application.Registries;
using Sonrisa.Application.Services;
using Sonrisa.Domain.Categories.Market;
using Sonrisa.Domain.Entities;
using Sonrisa.Infrastructure.Sources.Market;
using Sonrisa.Tests.Unit.Application;

namespace Sonrisa.Tests.Unit.Categories;

/// <summary>AC8, AC6 (market part): re-arm, steps and the start state of spec §2.4, rule validation, message content.</summary>
public sealed class MarketCategoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private static readonly MarketCategory Market = new(new MarketAssetCatalog(
    [
        new MarketAsset("bitcoin", "Bitcoin", MarketAsset.Crypto),
        new MarketAsset("AAPL", "Apple (AAPL)", MarketAsset.Stock),
    ]));

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string Rule(string json)
    {
        var result = Market.ValidateRule(Json(json));
        Assert.True(result.IsValid, string.Join("; ", result.Errors.SelectMany(e => e.Value)));
        return result.DataJson!;
    }

    private static Event Move(double current, double previous = 0, string asset = "bitcoin") =>
        new("market", "coingecko", Guid.NewGuid().ToString("N"), $"move {current}", null, null, T0, T0, false,
            MarketCategory.Serialize(new MarketData(asset, "Bitcoin", previous, current, "test")));

    private static Alert AlertOf(string rule) => new("user-1", "BTC up", "market", rule, [1], T0);

    private sealed class Store(double? latest) : Sonrisa.Domain.Categories.IObservationStore
    {
        public double? Latest(string asset) => latest;
        public double? Update(string asset, double changePercent) => throw new NotSupportedException();
    }

    /// <summary>Feeds the values one by one; returns the values at which the alert fired.</summary>
    private static List<double> Fired(string rule, double? latestAtStart, params double[] values)
    {
        var state = Market.InitialState(rule, new Store(latestAtStart));
        var fired = new List<double>();
        foreach (var value in values)
        {
            var outcome = Market.Evaluate(Move(value), rule, state);
            state = outcome.NewStateJson ?? state;
            if (outcome.Matched)
            {
                fired.Add(value);
            }
        }

        return fired;
    }

    // --- AC8: the example of spec §2.4 --------------------------------------------------------------------------

    [Fact]
    public void The_documented_sequence_notifies_exactly_three_times_at_the_documented_points()
    {
        var rule = Rule("""{"asset":"bitcoin","direction":"up","thresholdPercent":5,"stepPercent":2.5}""");

        // 2.0 (start) → 5.2 notifies; 5.8 nothing; 7.6 notifies (level 7.5); 4.5 nothing; 3.9 re-arms; 5.1 notifies.
        var fired = Fired(rule, 2.0, 5.2, 5.8, 7.6, 4.5, 3.9, 5.1);

        Assert.Equal([5.2, 7.6, 5.1], fired);
    }

    [Fact]
    public void An_alert_created_while_the_change_is_past_the_threshold_waits_for_a_new_crossing()
    {
        var rule = Rule("""{"asset":"bitcoin","direction":"up","thresholdPercent":5}""");

        // Latest 6.0 is already past 5: no notification for it, nor for 6.4 or 5.5; it re-arms below 4 only.
        Assert.Empty(Fired(rule, 6.0, 6.4, 5.5, 4.5));
        Assert.Equal([5.2], Fired(rule, 6.0, 6.4, 3.9, 5.2));
    }

    [Fact]
    public void A_start_past_a_step_level_escalates_only_at_the_next_level()
    {
        var rule = Rule("""{"asset":"bitcoin","direction":"up","thresholdPercent":5,"stepPercent":2.5}""");

        // Started at 7.6 → level 7.5 is already reached; 9.9 is below 10; 10.0 is the next level.
        Assert.Equal([10.0], Fired(rule, 7.6, 7.9, 9.9, 10.0));
    }

    [Fact]
    public void A_value_hovering_at_the_threshold_does_not_flap()
    {
        var rule = Rule("""{"asset":"bitcoin","direction":"up","thresholdPercent":5}""");

        Assert.Equal([5.1], Fired(rule, 2.0, 5.1, 4.9, 5.1, 4.5, 5.0, 4.0, 4.9));
    }

    [Fact]
    public void Without_an_observation_the_first_event_decides_the_start_and_a_move_under_way_does_not_notify()
    {
        // S5 review finding 2: created while the provider failed and bitcoin sat at +7%. The first event (7.1 → 7.6)
        // carries the previous value 7.1, which is already past 5, so the alert starts disarmed and stays silent.
        var rule = Rule("""{"asset":"bitcoin","direction":"up","thresholdPercent":5}""");
        var state = Market.InitialState(rule, new Store(null));
        Assert.True(MarketCategory.DeserializeState(state!).StartPending);

        var first = Market.Evaluate(Move(7.6, previous: 7.1), rule, state);
        Assert.False(first.Matched);
        var after = MarketCategory.DeserializeState(first.NewStateJson!);
        Assert.Equal(new MarketState(new MarketSide(false, 5), null), after);

        // It notifies again only after a re-arm.
        var rearmed = Market.Evaluate(Move(3.9, previous: 4.6), rule, first.NewStateJson);
        Assert.True(Market.Evaluate(Move(5.1, previous: 3.9), rule, rearmed.NewStateJson).Matched);
    }

    [Fact]
    public void Without_an_observation_a_first_event_that_crosses_the_threshold_notifies()
    {
        var rule = Rule("""{"asset":"bitcoin","direction":"down","thresholdPercent":5}""");
        var state = Market.InitialState(rule, new Store(null));

        var outcome = Market.Evaluate(Move(-5.0, previous: -4.9), rule, state);

        Assert.True(outcome.Matched);
        Assert.False(MarketCategory.DeserializeState(outcome.NewStateJson!).StartPending);
    }

    [Fact]
    public void Exactly_the_threshold_notifies_and_a_value_just_below_does_not()
    {
        var rule = Rule("""{"asset":"bitcoin","direction":"up","thresholdPercent":5}""");

        Assert.Empty(Fired(rule, 2.0, 4.9));
        Assert.Equal([5.0], Fired(rule, 2.0, 5.0));
    }

    [Fact]
    public void Down_mirrors_up_and_either_tracks_both_sides_apart()
    {
        var down = Rule("""{"asset":"bitcoin","direction":"down","thresholdPercent":5}""");
        Assert.Equal([-5.2, -5.1], Fired(down, 0, 6.0, -5.2, -5.8, -3.9, -5.1));

        var either = Rule("""{"asset":"bitcoin","direction":"either","thresholdPercent":5}""");
        Assert.Equal([5.5, -5.5], Fired(either, 0, 5.5, 6.0, -5.5, -6.0));
    }

    [Fact]
    public void An_event_of_another_asset_is_ignored_and_changes_no_state()
    {
        var rule = Rule("""{"asset":"bitcoin","direction":"up","thresholdPercent":5}""");
        var state = Market.InitialState(rule, new Store(0));

        var outcome = Market.Evaluate(Move(9, asset: "AAPL"), rule, state);

        Assert.False(outcome.Matched);
        Assert.Null(outcome.NewStateJson);
    }

    [Fact]
    public void An_unchanged_state_is_not_returned_again()
    {
        var rule = Rule("""{"asset":"bitcoin","direction":"up","thresholdPercent":5}""");
        var state = Market.InitialState(rule, new Store(0));

        Assert.Null(Market.Evaluate(Move(1.0), rule, state).NewStateJson);
    }

    [Theory]
    [InlineData(0, 0.4, false)]
    [InlineData(0.4, 0.5, true)]
    [InlineData(0.6, 0.9, false)]
    [InlineData(0.9, 1.0, true)]
    [InlineData(1.2, -0.1, true)]
    [InlineData(-0.4, -0.6, true)]
    [InlineData(-0.6, -0.9, false)]
    // S5 review finding 1: an exact line counts as reached on both sides of zero.
    [InlineData(4.9, 5.0, true)]
    [InlineData(5.0, 4.6, true)]
    [InlineData(-4.9, -5.0, true)]
    [InlineData(-5.0, -4.6, true)]
    [InlineData(-0.4, -0.5, true)]
    [InlineData(-5.0, -5.4, false)]
    [InlineData(5.0, 5.4, false)]
    public void The_grid_is_crossed_when_a_half_point_line_lies_between_the_two_values(double previous,
        double current, bool crossed) =>
        Assert.Equal(crossed, MarketLogic.CrossedGrid(previous, current));

    // --- Rule and simulated data ---------------------------------------------------------------------------------

    [Fact]
    public void A_rule_is_normalized_and_the_asset_key_is_canonical()
    {
        var json = Rule("""{"asset":" BITCOIN ","direction":"UP","thresholdPercent":5,"stepPercent":2.5}""");

        var rule = MarketCategory.DeserializeRule(json);
        Assert.Equal(new MarketRule("bitcoin", "up", 5, 2.5), rule);
    }

    [Theory]
    [InlineData("""{"asset":"doge","direction":"up","thresholdPercent":5}""", "asset")]
    [InlineData("""{"asset":"bitcoin","direction":"sideways","thresholdPercent":5}""", "direction")]
    [InlineData("""{"asset":"bitcoin","direction":"up"}""", "thresholdPercent")]
    [InlineData("""{"asset":"bitcoin","direction":"up","thresholdPercent":0.5}""", "thresholdPercent")]
    [InlineData("""{"asset":"bitcoin","direction":"up","thresholdPercent":5.2}""", "thresholdPercent")]
    [InlineData("""{"asset":"bitcoin","direction":"up","thresholdPercent":101}""", "thresholdPercent")]
    [InlineData("""{"asset":"bitcoin","direction":"up","thresholdPercent":5,"stepPercent":0.3}""", "stepPercent")]
    [InlineData("""{"asset":"bitcoin","direction":"up","thresholdPercent":5,"stepPercent":0}""", "stepPercent")]
    public void A_bad_rule_is_rejected_with_the_field_name(string rule, string field)
    {
        var result = Market.ValidateRule(Json(rule));

        Assert.False(result.IsValid);
        Assert.Contains(field, result.Errors.Keys);
    }

    [Fact]
    public void A_rule_that_is_not_an_object_is_rejected()
    {
        Assert.False(Market.ValidateRule(Json("[]")).IsValid);
    }

    [Fact]
    public void Simulated_data_needs_a_known_asset_and_both_changes()
    {
        var ok = Market.ValidateSimulatedData(Json(
            """{"asset":"aapl","previousChangePercent":4.6,"currentChangePercent":5.2}"""));
        Assert.True(ok.IsValid);
        var data = MarketCategory.Deserialize(ok.DataJson!);
        Assert.Equal(("AAPL", "Apple (AAPL)", 4.6, 5.2), (data.Asset, data.AssetName, data.PreviousChangePercent,
            data.CurrentChangePercent));

        var bad = Market.ValidateSimulatedData(Json("""{"asset":"nope","currentChangePercent":5.2}"""));
        Assert.Equal(["asset", "previousChangePercent"], bad.Errors.Keys.Order());
        Assert.Contains("currentChangePercent", Market.ValidateSimulatedData(
            Json("""{"asset":"bitcoin","previousChangePercent":1,"currentChangePercent":2000}""")).Errors.Keys);
    }

    // --- Message content (spec §2.6, AC9, AC19) -----------------------------------------------------------------

    [Fact]
    public void A_market_message_has_the_move_the_rule_and_the_disclaimer()
    {
        var rule = Rule("""{"asset":"bitcoin","direction":"up","thresholdPercent":5,"stepPercent":2.5}""");
        var data = new MarketData("bitcoin", "Bitcoin", 4.6, 5.2, "rolling 24 h change");
        var item = new Event("market", "coingecko", "x1", MarketCategory.TitleOf(data), null,
            "https://example.test/btc", T0, T0, true, MarketCategory.Serialize(data));

        var content = Market.Describe(item, AlertOf(rule));

        Assert.Equal("Bitcoin: +5.2% (was +4.6%)", content.Title);
        Assert.Equal("Change", content.KeyLabel);
        Assert.Equal("+4.6% → +5.2% (rolling 24 h change)", content.KeyValue);
        Assert.True(content.IsSimulated);
        Assert.Contains("BTC up", content.WhyMatched);
        Assert.Contains("rises by at least 5.0%", content.WhyMatched);
        Assert.Contains("every further 2.5%", content.WhyMatched);
        Assert.StartsWith("/alerts/", content.AlertPath);
        Assert.Contains("Not financial advice", content.Disclaimer);
        Assert.False(Market.SupportsBackfill);
        Assert.False(string.IsNullOrWhiteSpace(Market.BackfillUnavailableReason));
    }

    [Fact]
    public void Numbers_in_the_message_do_not_depend_on_the_server_culture()
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("hu-HU");
        try
        {
            Assert.Equal("+5.2%", MarketCategory.Signed(5.2));
            Assert.Equal("-0.5%", MarketCategory.Signed(-0.5));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = old;
        }
    }

    // --- The ingestor saves the state with the event (ADR-3, ADR-5) -----------------------------------------------

    [Fact]
    public async Task The_ingestor_saves_the_new_state_and_queues_the_delivery_in_one_run()
    {
        var rule = Rule("""{"asset":"bitcoin","direction":"up","thresholdPercent":5}""");
        var repo = new EventIngestorTests.FakeRepository();
        var armed = Market.InitialState(rule, new Store(2.0));
        repo.Candidates.Add((new Sonrisa.Application.Abstractions.MatchCandidate(7, rule, [10], armed), T0.AddDays(-1),
            "market"));
        var ingestor = new EventIngestor(repo, repo, repo, new CategoryRegistry([Market]), repo, new TestClock(T0));
        var data = new MarketData("bitcoin", "Bitcoin", 4.6, 5.2, "test");

        var result = await ingestor.IngestAsync("coingecko", "market",
            new Sonrisa.Application.Abstractions.IncomingEvent("bitcoin:1", "t", null, null, T0,
                MarketCategory.Serialize(data)), false, CancellationToken.None);

        Assert.Equal(1, result.DeliveriesCreated);
        Assert.False(MarketCategory.DeserializeState(repo.States[7]).Up!.Armed);
    }

    [Fact]
    public async Task A_rearm_without_a_match_still_saves_the_state()
    {
        var rule = Rule("""{"asset":"bitcoin","direction":"up","thresholdPercent":5}""");
        var repo = new EventIngestorTests.FakeRepository();
        var disarmed = Market.InitialState(rule, new Store(6.0));
        repo.Candidates.Add((new Sonrisa.Application.Abstractions.MatchCandidate(7, rule, [10], disarmed),
            T0.AddDays(-1), "market"));
        var ingestor = new EventIngestor(repo, repo, repo, new CategoryRegistry([Market]), repo, new TestClock(T0));

        var result = await ingestor.IngestAsync("coingecko", "market",
            new Sonrisa.Application.Abstractions.IncomingEvent("bitcoin:2", "t", null, null, T0,
                MarketCategory.Serialize(new MarketData("bitcoin", "Bitcoin", 4.5, 3.5, "test"))), false,
            CancellationToken.None);

        Assert.Equal(0, result.DeliveriesCreated);
        Assert.True(MarketCategory.DeserializeState(repo.States[7]).Up!.Armed);
    }
}
