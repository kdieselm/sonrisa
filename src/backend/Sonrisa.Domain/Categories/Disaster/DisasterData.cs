namespace Sonrisa.Domain.Categories.Disaster;

/// <summary>
/// Data of a disaster event (spec §2.3). Coordinates are stored for a possible distance rule (D24); the current rule
/// uses the magnitude and the place text only.
/// </summary>
public sealed record DisasterData(double Magnitude, string Place, double? Latitude, double? Longitude, double? DepthKm)
{
    public const int MaxPlaceLength = 200;
}
