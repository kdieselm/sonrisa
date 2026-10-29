using Sonrisa.Domain.Categories.Market;
using Sonrisa.Infrastructure.Configuration;
using Sonrisa.Infrastructure.Sources.Market;

namespace Sonrisa.Tests.Unit.Sources;

/// <summary>AC9 (CoinGecko part) and the market observation logic of spec §2.4.</summary>
public sealed class MarketSourcesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private static readonly MarketAsset Bitcoin = new("bitcoin", "Bitcoin", MarketAsset.Crypto);
    private static readonly MarketAsset Ethereum = new("ethereum", "Ethereum", MarketAsset.Crypto);
    private static readonly MarketAsset Solana = new("solana", "Solana", MarketAsset.Crypto);

    [Fact]
    public void The_recorded_coingecko_response_is_parsed_into_observations()
    {
        var readings = CoinGeckoSource.Parse(Fixture.Read("coingecko-simple-price.json"), [Bitcoin, Ethereum, Solana]);

        Assert.Equal(["bitcoin", "ethereum", "solana"], readings.Select(r => r.Asset.Key));
        var bitcoin = readings.Single(r => r.Asset.Key == "bitcoin");
        Assert.Equal(-2.821719315826033, bitcoin.ChangePercent, 12); // usd_24h_change of the recording
        Assert.All(readings, r => Assert.True(double.IsFinite(r.ChangePercent)));
    }

    [Fact]
    public void A_missing_asset_or_change_value_is_skipped_not_fatal()
    {
        var readings = CoinGeckoSource.Parse(
            """{"bitcoin":{"usd":1},"ethereum":{"usd":2,"usd_24h_change":null},"solana":{"usd_24h_change":1.5}}""",
            [Bitcoin, Ethereum, Solana]);

        Assert.Equal("solana", Assert.Single(readings).Asset.Key);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"rate limited\"")]
    public void An_answer_that_is_not_an_object_fails_the_fetch(string json) =>
        Assert.Throws<FormatException>(() => CoinGeckoSource.Parse(json, [Bitcoin]));

    [Fact]
    public void An_error_object_yields_no_observations()
    {
        // CoinGecko answers 429 with {"status":{"error_code":429,...}}; the HTTP status already fails the fetch.
        Assert.Empty(CoinGeckoSource.Parse("""{"status":{"error_code":429}}""", [Bitcoin]));
    }

    [Theory]
    [InlineData("""{"c":261.74,"d":-1.1,"dp":-0.4185,"h":263,"l":259,"o":261,"pc":262.84,"t":1582641000}""", -0.4185)]
    [InlineData("""{"c":0,"d":null,"dp":null,"h":0,"l":0,"o":0,"pc":0,"t":0}""", null)]
    [InlineData("""{"error":"Invalid API key"}""", null)]
    public void A_finnhub_quote_gives_dp_or_nothing(string json, double? expected) =>
        Assert.Equal(expected, FinnhubSource.ParseQuote(json));

    // --- MarketMovements -------------------------------------------------------------------------------------------

    private static (MarketMovements Movements, InMemoryObservationStore Store, TestClock Clock) Movements()
    {
        var store = new InMemoryObservationStore();
        var clock = new TestClock(T0);
        return (new MarketMovements(store, clock), store, clock);
    }

    [Fact]
    public void The_first_poll_emits_nothing_but_remembers_the_value()
    {
        var (movements, store, _) = Movements();

        var events = movements.Record([new AssetReading(Bitcoin, 5.2)], "test", _ => null);

        Assert.Empty(events);
        Assert.Equal(5.2, store.Latest("bitcoin"));
    }

    [Fact]
    public void A_crossing_of_the_half_point_grid_emits_one_event_with_both_values()
    {
        var (movements, _, clock) = Movements();
        movements.Record([new AssetReading(Bitcoin, 4.6)], "test", _ => null);
        clock.Advance(TimeSpan.FromMinutes(5));

        var item = Assert.Single(movements.Record([new AssetReading(Bitcoin, 5.2)], "rolling", a => $"https://x/{a.Key}"));

        var data = MarketCategory.Deserialize(item.DataJson);
        Assert.Equal(("bitcoin", "Bitcoin", 4.6, 5.2, "rolling"),
            (data.Asset, data.AssetName, data.PreviousChangePercent, data.CurrentChangePercent, data.Baseline));
        Assert.Equal("Bitcoin: +5.2% (was +4.6%)", item.Title);
        Assert.Equal("https://x/bitcoin", item.Url);
        Assert.Equal(clock.GetUtcNow(), item.PublishedAt);
        Assert.StartsWith("bitcoin:", item.ExternalId);
    }

    [Fact]
    public void A_move_inside_one_grid_cell_emits_nothing_but_updates_the_value()
    {
        var (movements, store, _) = Movements();
        movements.Record([new AssetReading(Bitcoin, 4.6)], "test", _ => null);

        Assert.Empty(movements.Record([new AssetReading(Bitcoin, 4.9)], "test", _ => null));
        Assert.Equal(4.9, store.Latest("bitcoin"));
    }

    [Fact]
    public void Two_polls_at_different_times_give_different_external_ids()
    {
        var (movements, _, clock) = Movements();
        movements.Record([new AssetReading(Bitcoin, 1.0)], "test", _ => null);
        clock.Advance(TimeSpan.FromMinutes(1));
        var first = movements.Record([new AssetReading(Bitcoin, 2.0)], "test", _ => null).Single();
        clock.Advance(TimeSpan.FromMinutes(1));
        var second = movements.Record([new AssetReading(Bitcoin, 3.0)], "test", _ => null).Single();

        Assert.NotEqual(first.ExternalId, second.ExternalId);
    }

    [Fact]
    public void The_mock_stocks_are_simulated_stay_in_range_and_eventually_move()
    {
        var (movements, store, _) = Movements();
        var aapl = new MarketAsset("AAPL", "Apple (AAPL)", MarketAsset.Stock);
        var options = new MockStocksOptions { MaxStepPercent = 0.6 };
        var source = new MockStocksSource(movements, options, [aapl], new Random(42));

        var emitted = 0;
        for (var i = 0; i < 200; i++)
        {
            emitted += source.FetchAsync(CancellationToken.None).Result.Count;
            Assert.InRange(store.Latest("AAPL")!.Value, -25, 25);
        }

        Assert.True(source.ProducesSimulatedEvents);
        Assert.InRange(emitted, 5, 200);
    }
}
