using System.Net;
using Sonrisa.Domain.Categories.Market;
using Sonrisa.Infrastructure.Configuration;
using Sonrisa.Infrastructure.Sources;
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

    // --- HTTP paths (S5 review finding 7) -------------------------------------------------------------------------

    private const string Secret = "secret-key-123";

    /// <summary>Answers each request with the status and body the function picks; records the requests.</summary>
    private sealed class FakeHandler(Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> answer)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var (status, body) = answer(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static string AllMessages(Exception ex)
    {
        var text = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
        {
            text.Add(e.ToString());
        }

        return string.Join("\n", text);
    }

    private static CoinGeckoSource CoinGecko(FakeHandler handler, InMemoryObservationStore? store = null) =>
        new(new SourceHttpClient(handler), new MarketMovements(store ?? new InMemoryObservationStore(),
            new TestClock(T0)), new CoinGeckoOptions { BaseUrl = "https://cg.test", ApiKey = Secret }, [Bitcoin]);

    private static FinnhubSource Finnhub(FakeHandler handler, InMemoryObservationStore store) =>
        new(new SourceHttpClient(handler), new MarketMovements(store, new TestClock(T0)),
            new FinnhubOptions { BaseUrl = "https://fh.test", ApiKey = Secret },
            [new MarketAsset("AAPL", "Apple", MarketAsset.Stock), new MarketAsset("MSFT", "Microsoft", MarketAsset.Stock)]);

    [Fact]
    public async Task A_coingecko_server_error_fails_the_fetch_without_the_key_in_the_error()
    {
        var source = CoinGecko(new FakeHandler(_ => (HttpStatusCode.InternalServerError, "oops")));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => source.FetchAsync(CancellationToken.None));
        Assert.DoesNotContain(Secret, AllMessages(ex));
    }

    [Fact]
    public async Task A_coingecko_error_object_with_status_200_is_a_failed_fetch_not_zero_items()
    {
        var store = new InMemoryObservationStore();
        var source = CoinGecko(new FakeHandler(_ => (HttpStatusCode.OK, """{"status":{"error_code":429}}""")), store);

        var ex = await Assert.ThrowsAsync<FormatException>(() => source.FetchAsync(CancellationToken.None));
        Assert.DoesNotContain(Secret, AllMessages(ex));
        Assert.Null(store.Latest("bitcoin"));
    }

    [Fact]
    public async Task One_finnhub_symbol_rate_limited_does_not_drop_the_others()
    {
        var store = new InMemoryObservationStore();
        var handler = new FakeHandler(r => r.RequestUri!.Query.Contains("AAPL")
            ? (HttpStatusCode.TooManyRequests, "{}")
            : (HttpStatusCode.OK, """{"dp":1.25}"""));

        await Finnhub(handler, store).FetchAsync(CancellationToken.None);

        Assert.Null(store.Latest("AAPL"));
        Assert.Equal(1.25, store.Latest("MSFT"));
        Assert.All(handler.Requests, r =>
        {
            Assert.DoesNotContain(Secret, r.RequestUri!.ToString());
            Assert.Equal(Secret, r.Headers.GetValues("X-Finnhub-Token").Single());
        });
    }

    [Fact]
    public async Task All_finnhub_symbols_failing_fails_the_fetch_without_the_key_in_the_error()
    {
        var source = Finnhub(new FakeHandler(_ => (HttpStatusCode.TooManyRequests, "{}")),
            new InMemoryObservationStore());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => source.FetchAsync(CancellationToken.None));
        Assert.DoesNotContain(Secret, AllMessages(ex));
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
    public void A_fall_onto_an_exact_negative_line_emits_an_event_that_a_down_alert_fires_on()
    {
        // S5 review finding 1, end to end through the source side: -4.9 → -5.0 must emit, and a down alert at 5 fires.
        var (movements, _, _) = Movements();
        movements.Record([new AssetReading(Bitcoin, -4.9)], "test", _ => null);

        var item = Assert.Single(movements.Record([new AssetReading(Bitcoin, -5.0)], "test", _ => null));

        var market = new MarketCategory(new MarketAssetCatalog([Bitcoin]));
        var rule = market.ValidateRule(System.Text.Json.JsonDocument.Parse(
            """{"asset":"bitcoin","direction":"down","thresholdPercent":5}""").RootElement).DataJson!;
        var stored = new Sonrisa.Domain.Entities.Event(MarketCategory.CategoryKey, "coingecko", item.ExternalId,
            item.Title, null, item.Url, item.PublishedAt!.Value, item.PublishedAt!.Value, false, item.DataJson);
        var state = market.InitialState(rule, new InMemoryObservationStore());
        Assert.True(market.Evaluate(stored, rule, state).Matched);
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
    public async Task The_mock_stocks_are_simulated_stay_in_range_and_eventually_move()
    {
        var (movements, store, _) = Movements();
        var aapl = new MarketAsset("AAPL", "Apple (AAPL)", MarketAsset.Stock);
        var options = new MockStocksOptions { MaxStepPercent = 0.6 };
        var source = new MockStocksSource(movements, options, [aapl], new Random(42));

        var emitted = 0;
        for (var i = 0; i < 200; i++)
        {
            emitted += (await source.FetchAsync(CancellationToken.None)).Count;
            Assert.InRange(store.Latest("AAPL")!.Value, -25, 25);
        }

        Assert.True(source.ProducesSimulatedEvents);
        Assert.InRange(emitted, 5, 200);
    }
}
