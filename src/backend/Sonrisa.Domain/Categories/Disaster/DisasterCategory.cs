using System.Text.Json;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Domain.Categories.Disaster;

/// <summary>Natural disasters; today earthquakes from USGS (spec §2.2, §2.5).</summary>
public sealed class DisasterCategory : IEventCategory
{
    public const string CategoryKey = "disaster";

    public string Key => CategoryKey;
    public string DisplayName => "Natural disaster";
    public TimeSpan? MaxAge => TimeSpan.FromHours(2);
    public bool SupportsBackfill => true;
    public string? BackfillUnavailableReason => null;

    /// <summary>The one way to write disaster data; sources use it so the shape has a single owner.</summary>
    public static string Serialize(DisasterData data) => JsonSerializer.Serialize(data, JsonText.Options);

    public static DisasterData Deserialize(string json) =>
        JsonSerializer.Deserialize<DisasterData>(json, JsonText.Options)
        ?? throw new JsonException("Disaster data is null.");

    public DataValidationResult ValidateSimulatedData(JsonElement data)
    {
        var errors = new FieldErrors();
        var input = errors.Read<SimulatedDisasterInput>(data);
        if (input is null)
        {
            return DataValidationResult.Invalid(errors.ToDictionary());
        }

        if (input.Magnitude is not { } magnitude || !double.IsFinite(magnitude) || magnitude is < 0 or > 10)
        {
            errors.Add("magnitude", "Magnitude is required, between 0 and 10.");
        }

        if (string.IsNullOrWhiteSpace(input.Place) || input.Place.Trim().Length > DisasterData.MaxPlaceLength)
        {
            errors.Add("place", $"Place is required, at most {DisasterData.MaxPlaceLength} characters.");
        }

        if (input.Latitude is null != input.Longitude is null)
        {
            errors.Add("latitude", "Give both latitude and longitude, or neither.");
        }

        if (input.Latitude is < -90 or > 90)
        {
            errors.Add("latitude", "Latitude must be between -90 and 90.");
        }

        if (input.Longitude is < -180 or > 180)
        {
            errors.Add("longitude", "Longitude must be between -180 and 180.");
        }

        if (input.DepthKm is < -10 or > 1000)
        {
            errors.Add("depthKm", "Depth must be between -10 and 1000 km.");
        }

        return errors.Any
            ? DataValidationResult.Invalid(errors.ToDictionary())
            : DataValidationResult.Valid(Serialize(new DisasterData(input.Magnitude!.Value, input.Place!.Trim(),
                input.Latitude, input.Longitude, input.DepthKm)));
    }

    public static DisasterRule DeserializeRule(string json) =>
        JsonSerializer.Deserialize<DisasterRule>(json, JsonText.Options)
        ?? throw new JsonException("Disaster rule is null.");

    public DataValidationResult ValidateRule(JsonElement rule)
    {
        var errors = new FieldErrors();
        var input = errors.Read<RuleInput>(rule, "rule");
        if (input is null)
        {
            return DataValidationResult.Invalid(errors.ToDictionary());
        }

        if (input.MinMagnitude is not { } min || !double.IsFinite(min) || min is < DisasterRule.FeedFloor or > 10)
        {
            errors.Add("minMagnitude", $"Minimum magnitude is required, between {DisasterRule.FeedFloor} and 10.");
        }

        var place = string.IsNullOrWhiteSpace(input.PlaceContains) ? null : input.PlaceContains.Trim();
        if (place is { Length: > DisasterRule.MaxPlaceContainsLength })
        {
            errors.Add("placeContains", $"Place text is at most {DisasterRule.MaxPlaceContainsLength} characters.");
        }

        GeoCircle? near = null;
        if (input.Near is { } n)
        {
            var before = errors.Any;
            if (n.Latitude is not { } lat || !double.IsFinite(lat) || lat is < -90 or > 90)
            {
                errors.Add("near.latitude", "Latitude is required, between -90 and 90.");
            }

            if (n.Longitude is not { } lon || !double.IsFinite(lon) || lon is < -180 or > 180)
            {
                errors.Add("near.longitude", "Longitude is required, between -180 and 180.");
            }

            if (n.RadiusKm is not { } radius || !double.IsFinite(radius)
                || radius is < GeoCircle.MinRadiusKm or > GeoCircle.MaxRadiusKm)
            {
                errors.Add("near.radiusKm",
                    $"Radius is required, between {GeoCircle.MinRadiusKm} and {GeoCircle.MaxRadiusKm:0} km.");
            }

            if (!errors.Any && !before)
            {
                near = new GeoCircle(n.Latitude!.Value, n.Longitude!.Value, n.RadiusKm!.Value);
            }
        }

        return errors.Any
            ? DataValidationResult.Invalid(errors.ToDictionary())
            : DataValidationResult.Valid(JsonSerializer.Serialize(
                new DisasterRule(input.MinMagnitude!.Value, place, near), JsonText.Options));
    }

    /// <summary>
    /// Magnitude at least the minimum; place contains the text (case-insensitive, invariant); epicentre within the
    /// circle. An event without coordinates never matches a rule with a circle (D27).
    /// </summary>
    public bool Matches(Event item, string ruleJson)
    {
        var data = Deserialize(item.Data);
        var rule = DeserializeRule(ruleJson);
        if (data.Magnitude < rule.MinMagnitude)
        {
            return false;
        }

        if (rule.PlaceContains is { } text
            && !data.Place.Contains(text, StringComparison.InvariantCultureIgnoreCase))
        {
            return false;
        }

        return rule.Near is not { } circle
               || (data is { Latitude: { } lat, Longitude: { } lon } && circle.Contains(lat, lon));
    }

    private sealed record RuleInput(double? MinMagnitude, string? PlaceContains, CircleInput? Near);

    private sealed record CircleInput(double? Latitude, double? Longitude, double? RadiusKm);

    private sealed record SimulatedDisasterInput(double? Magnitude, string? Place, double? Latitude,
        double? Longitude, double? DepthKm);
}
