using System.Text.Json;
using Sonrisa.Application.Abstractions;
using Sonrisa.Domain.Categories.Disaster;
using Sonrisa.Infrastructure.Configuration;

namespace Sonrisa.Infrastructure.Sources.Usgs;

/// <summary>
/// USGS earthquake summary feed, GeoJSON (spec §2.5, F6). External ID = feature <c>id</c>, the event's preferred ID;
/// <c>properties.ids</c> (every network's ID of the same quake) become alternate IDs, so a quake whose preferred ID
/// changes later still updates its row (T9, review 2026-10-29). Published time = origin time (<c>properties.time</c>).
/// </summary>
public sealed class UsgsSource(SourceHttpClient http, UsgsOptions options) : IEventSource
{
    public const string SourceKey = "usgs";

    public string Key => SourceKey;
    public string DisplayName => "USGS earthquakes";
    public string CategoryKey => DisasterCategory.CategoryKey;
    public TimeSpan Interval => options.Interval;

    public async Task<IReadOnlyList<IncomingEvent>> FetchAsync(CancellationToken ct)
    {
        var json = await http.Http.GetStringAsync(options.Url, ct);
        return Parse(json);
    }

    /// <summary>Parses a feed; a feature that is malformed or lacks ID, magnitude or title is skipped, not fatal.</summary>
    public static IReadOnlyList<IncomingEvent> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("features", out var features)
            || features.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("USGS response has no 'features' array.");
        }

        var result = new List<IncomingEvent>();
        foreach (var feature in features.EnumerateArray())
        {
            IncomingEvent? item;
            try
            {
                item = TryParseFeature(feature);
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
            {
                item = null; // one bad feature must not drop the rest of the feed
            }

            if (item is not null)
            {
                result.Add(item);
            }
        }

        return result;
    }

    private static IncomingEvent? TryParseFeature(JsonElement feature)
    {
        if (feature.ValueKind != JsonValueKind.Object
            || String(feature, "id") is not { } id
            || !feature.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object
            || Number(properties, "mag") is not { } magnitude
            || String(properties, "title") is not { } title)
        {
            return null;
        }

        var place = String(properties, "place") ?? title;
        if (place.Length > DisasterData.MaxPlaceLength)
        {
            place = place[..DisasterData.MaxPlaceLength];
        }

        DateTimeOffset? publishedAt = properties.TryGetProperty("time", out var time)
                                      && time.ValueKind == JsonValueKind.Number
                                      && time.TryGetInt64(out var ms)
                                      && ms is >= MinUnixMs and <= MaxUnixMs
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null;

        // ",us7000abcd,pt26123456," → every ID of this quake except the preferred one.
        var alternateIds = (String(properties, "ids") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(other => other != id)
            .ToList();

        double? longitude = null, latitude = null, depth = null;
        if (feature.TryGetProperty("geometry", out var geometry)
            && geometry.ValueKind == JsonValueKind.Object
            && geometry.TryGetProperty("coordinates", out var coordinates)
            && coordinates.ValueKind == JsonValueKind.Array
            && coordinates.GetArrayLength() >= 2)
        {
            // GeoJSON order: longitude, latitude, depth (km).
            longitude = NumberAt(coordinates, 0);
            latitude = NumberAt(coordinates, 1);
            depth = coordinates.GetArrayLength() >= 3 ? NumberAt(coordinates, 2) : null;
        }

        var data = DisasterCategory.Serialize(new DisasterData(magnitude, place, latitude, longitude, depth));
        return new IncomingEvent(id, title, null, String(properties, "url"), publishedAt, data, alternateIds);
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? Finite(value) : null;

    private static double? NumberAt(JsonElement array, int index) => Finite(array[index]);

    private static double? Finite(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;

    // DateTimeOffset's range in Unix milliseconds.
    private const long MinUnixMs = -62135596800000;
    private const long MaxUnixMs = 253402300799999;
}
