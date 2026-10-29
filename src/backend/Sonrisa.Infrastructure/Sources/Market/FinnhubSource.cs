using System.Text.Json;
using Sonrisa.Application.Abstractions;
using Sonrisa.Domain.Categories.Market;
using Sonrisa.Infrastructure.Configuration;

namespace Sonrisa.Infrastructure.Sources.Market;

/// <summary>
/// Finnhub stock quotes (spec §2.5, F2, F9), only registered when <c>FINNHUB_API_KEY</c> is set. One <c>/quote</c>
/// call per symbol; <c>dp</c> is the change in percent. What <c>dp</c> is relative to is unverified (spec §8). The key
/// travels in a header, never in the URL, so it cannot reach a log or an exception message.
/// </summary>
public sealed class FinnhubSource(
    SourceHttpClient http,
    MarketMovements movements,
    FinnhubOptions options,
    IReadOnlyList<MarketAsset> assets) : IEventSource
{
    public const string SourceKey = "finnhub";
    public const string Baseline = "change in percent (Finnhub dp)";

    public string Key => SourceKey;
    public string DisplayName => "Finnhub";
    public string CategoryKey => MarketCategory.CategoryKey;
    public TimeSpan Interval => options.Interval;

    public async Task<IReadOnlyList<IncomingEvent>> FetchAsync(CancellationToken ct)
    {
        var readings = new List<AssetReading>();
        Exception? lastError = null;
        foreach (var asset in assets)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    $"{options.BaseUrl.TrimEnd('/')}/quote?symbol={Uri.EscapeDataString(asset.Key)}");
                request.Headers.Add("X-Finnhub-Token", options.ApiKey);
                using var response = await http.Http.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();
                if (ParseQuote(await response.Content.ReadAsStringAsync(ct)) is { } percent)
                {
                    readings.Add(new AssetReading(asset, percent));
                }
            }
            catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or FormatException
                                           or JsonException) && !ct.IsCancellationRequested)
            {
                lastError = ex; // one symbol failing (a rate limit) must not drop the others
            }
        }

        if (readings.Count == 0 && lastError is not null)
        {
            throw new InvalidOperationException("Finnhub: no quote could be read.", lastError);
        }

        return movements.Record(readings, Baseline, _ => null);
    }

    /// <summary>The <c>dp</c> field of a quote, or null when it is missing or null (an unknown symbol).</summary>
    public static double? ParseQuote(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Object
               && document.RootElement.TryGetProperty("dp", out var dp)
               && dp.ValueKind == JsonValueKind.Number
               && dp.TryGetDouble(out var percent)
               && double.IsFinite(percent)
            ? percent
            : null;
    }
}
