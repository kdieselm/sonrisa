namespace Sonrisa.Domain.Categories.Disaster;

/// <summary>
/// Data of a disaster event (spec §2.3). The coordinates feed the optional point + radius of the rule (D24, D27); an
/// event without them never matches a rule with a radius.
/// </summary>
public sealed record DisasterData(double Magnitude, string Place, double? Latitude, double? Longitude, double? DepthKm)
{
    public const int MaxPlaceLength = 200;
}
