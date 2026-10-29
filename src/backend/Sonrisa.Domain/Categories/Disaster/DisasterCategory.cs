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

    private sealed record SimulatedDisasterInput(double? Magnitude, string? Place, double? Latitude,
        double? Longitude, double? DepthKm);
}
