# How to add an event source

A **source adapter** fetches one external feed and turns its items into normalized events of one category (spec §2.5, §2.11). The poller runs every source in its own loop on its own interval. A source that throws is logged and polled again on the next interval; it never stops the other sources (T26, AC17).

- **Status:** S5 (the observation-source section was added with the market sources). S6 checks every path and registration named here against the repository.
- **Worked examples:** `Sonrisa.Infrastructure/Sources/Usgs/UsgsSource.cs` (JSON) and `Sources/Rss/RssSource.cs` (XML, one class configured for several feeds); `Sources/Market/CoinGeckoSource.cs` (a value per asset).

## Two cases

1. **Another feed of a format we already parse.** For example, one more RSS news feed. Add a configuration entry only, with no code change:

   ```json
   "Sources": { "Rss": [ { "Key": "aljazeera", "DisplayName": "Al Jazeera", "Url": "https://…/rss.xml", "Interval": "00:05:00" } ] }
   ```

   In Compose it can also be set through environment variables (`Sources__Rss__2__Key=…`).

2. **A feed with a backup feed.** Use `Sources:RssFailover`, as the news source does (D26): a primary and a fallback feed, retries of the primary within a poll, and a circuit breaker that skips the primary for a while after repeated failures:

   ```json
   "Sources": { "RssFailover": [ {
     "Key": "world-news", "DisplayName": "World news (Guardian, BBC fallback)", "Interval": "00:05:00",
     "Primary": { "Name": "The Guardian – World", "Url": "https://www.theguardian.com/world/rss" },
     "Fallback": { "Name": "BBC News – World", "Url": "https://feeds.bbci.co.uk/news/world/rss.xml" },
     "Attempts": 3, "RetryDelays": [ "00:00:02", "00:00:05" ], "FailuresToOpen": 2, "OpenDuration": "00:15:00"
   } ] }
   ```

   For another format, `FailoverSource` and `CircuitBreaker` (`Infrastructure/Sources/Resilience/`) take any two `IEventSource`s of the same category; register them as the RSS block in `SourceRegistration.cs` does.

3. **A new feed format.** Follow the rest of this guide.

## What you add (new format)

| Layer | File(s) to add | What it holds |
|---|---|---|
| Infrastructure | `Sonrisa.Infrastructure/Sources/<Name>/<Name>Source.cs` | `IEventSource`: `Key`, `DisplayName`, `CategoryKey`, `Interval`, `FetchAsync`, plus a static `Parse(string)` that the tests call without the network |
| Infrastructure | `Sonrisa.Infrastructure/Configuration/SourceOptions.cs` | An options class for the feed (URL, interval, `Enabled`), as a property of `SourcesOptions` |
| Api | `Sonrisa.Api/appsettings.json` | Default values under `Sources:<Name>` |
| Tests | `Sonrisa.Tests/Fixtures/<name>.<ext>` and `Sonrisa.Tests/Unit/Sources/<Name>SourceTests.cs` | A **recorded real response** (fetch it once with `curl`, note the date in `process/log.md`) and parser tests on it |

Rules for the adapter:

- **External ID:** must be stable across polls for the same item. The pair (source key, external ID) is unique, and a known pair updates the row instead of creating a new event (spec §2.3). If the feed can give the same item another ID later, pass the other IDs as `AlternateIds` (USGS does this with `properties.ids`). IDs longer than 500 characters are hashed by the ingestor, so you need not shorten them.
- **Published time:** the feed's own time for the item, or `null` if it has none (the ingestor then uses the first-seen time). It drives the category's age limit.
- **Data:** build the category's data record and write it with the category's `Serialize` (e.g. `DisasterCategory.Serialize(new DisasterData(...))`).
- **One bad item is not fatal:** skip items without an ID or title, or with wrong types, inside `Parse` (see the per-feature guard in `UsgsSource`). A response that is not the expected format at all should throw, so the poller logs it. An item the database rejects is logged and skipped by the poller; the other items of the poll are still stored.
- **HTTP:** use the shared `SourceHttpClient` (timeout, response-size limit, user agent). Do not create your own `HttpClient`.
- **XML:** keep `DtdProcessing.Prohibit` as in `RssSource`.

## A source that observes values (market)

Some sources do not return events but a **current value** per asset (CoinGecko, Finnhub, the mock). Spec §2.4 turns two consecutive values into an event only when a 0.5-point grid line was crossed. That part is shared, so such a source only has to read its values:

1. Parse the response into `AssetReading(MarketAsset, ChangePercent)` items (a static `Parse` that the tests call on a recorded response; `CoinGeckoSource.Parse`, `FinnhubSource.ParseQuote`).
2. Return `movements.Record(readings, baseline, url)` from `FetchAsync`. `MarketMovements` (`Infrastructure/Sources/Market/`) stores each value in `IObservationStore`, emits nothing on the first poll, and builds the `IncomingEvent` (external ID = asset + poll time, title, data) on a crossing. `baseline` says what the percentage is relative to and goes into the message.
3. A source that generates its data (the mock) sets `ProducesSimulatedEvents => true`, so its events carry the "simulated" label.
4. A secret goes in a request **header** (`X-Finnhub-Token`, `x-cg-demo-api-key`), never in the URL, so it cannot reach a log or an exception message.

Options live in `Sources:Market` (`MarketOptions` in `SourceOptions.cs`); the assets are a list in `appsettings.json`, with `Kind` (`crypto` or `stock`) deciding which source reads them. The registration is the same kind of block as below, with the assets of the right kind (`SourceRegistration.cs`: CoinGecko, Finnhub if `Sources:Market:Finnhub:ApiKey` is set, otherwise the mock). A key from the environment is passed in `docker-compose.yml` and listed, empty, in `.env.example`.

## The one registration

One block in `Sonrisa.Api/Configuration/SourceRegistration.cs`, under `// Sources`:

```csharp
if (options.Weather.Enabled)
{
    services.AddSingleton<IEventSource>(sp =>
        new WeatherSource(sp.GetRequiredService<SourceHttpClient>(), options.Weather));
}
```

If the source belongs to a new category, add the category first ([`add-a-category.md`](add-a-category.md)).

## What you get without further changes

- Polling on the configured interval, starting at app start, with per-source error isolation and logging.
- Insert or update per item in its own transaction (`EventIngestor`).
- The source appears in the admin event filter and in the event list with its display name.

## Checklist

- [ ] The key is new and not `simulated`, which is reserved for the admin form (`RegistryTests`).
- [ ] The category key is registered; otherwise the app does not start.
- [ ] Parser tests on the recorded response pass. The tests never call the real feed: the test host sets `Sources:PollingEnabled=false`.
- [ ] After `docker compose up`, the API log shows `Source <key>: N items, … new`, and the events appear in the admin list.
