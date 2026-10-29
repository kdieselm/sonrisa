using System.Text.Json;
using Sonrisa.Application.Abstractions;
using Sonrisa.Domain.Categories.Market;
using Sonrisa.Infrastructure.Configuration;

namespace Sonrisa.Infrastructure.Sources.Market;

/// <summary>
/// CoinGecko crypto prices (spec §2.5, F1): one batched <c>simple/price</c> call, <c>usd_24h_change</c> = the rolling
/// 24 hour change in percent. Works without a key; a Demo key is sent as a header when configured.
/// </summary>
public sealed class CoinGeckoSource(
    SourceHttpClient http,
    MarketMovements movements,
    CoinGeckoOptions options,
    IReadOnlyList<MarketAsset> assets) : IEventSource
{
    public const string SourceKey = "coingecko";
    public const string Baseline = "rolling 24 h change";

    public string Key => SourceKey;
    public string DisplayName => "CoinGecko";
    public string CategoryKey => MarketCategory.CategoryKey;
    public TimeSpan Interval => options.Interval;

    public async Task<IReadOnlyList<IncomingEvent>> FetchAsync(CancellationToken ct)
    {
        var ids = string.Join(',', assets.Select(a => Uri.EscapeDataString(a.Key)));
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{options.BaseUrl.TrimEnd('/')}/api/v3/simple/price?ids={ids}&vs_currencies=usd&include_24hr_change=true");
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            request.Headers.Add("x-cg-demo-api-key", options.ApiKey);
        }

        using var response = await http.Http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var readings = Parse(await response.Content.ReadAsStringAsync(ct), assets);
        return movements.Record(readings, Baseline, asset => $"https://www.coingecko.com/en/coins/{asset.Key}");
    }

    /// <summary>
    /// Parses a <c>simple/price</c> response. An asset that is missing, or has no change value, is skipped, so one
    /// bad entry does not drop the rest. An answer that is not a JSON object is an error.
    /// </summary>
    public static IReadOnlyList<AssetReading> Parse(string json, IEnumerable<MarketAsset> assets)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("CoinGecko response is not a JSON object.");
        }

        var readings = new List<AssetReading>();
        foreach (var asset in assets)
        {
            if (document.RootElement.TryGetProperty(asset.Key, out var entry)
                && entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("usd_24h_change", out var change)
                && change.ValueKind == JsonValueKind.Number
                && change.TryGetDouble(out var percent)
                && double.IsFinite(percent))
            {
                readings.Add(new AssetReading(asset, percent));
            }
        }

        return readings;
    }
}
