namespace Sonrisa.Domain.Categories.Disaster;

/// <summary>
/// Rule of a disaster alert (spec §2.2, D27): a minimum magnitude, optionally a text the USGS place must contain and
/// optionally a circle the epicentre must lie in. Every condition that is set must hold.
/// </summary>
public sealed record DisasterRule(double MinMagnitude, string? PlaceContains, GeoCircle? Near)
{
    /// <summary>The feed floor: the USGS <c>4.5_day</c> feed has nothing below it (T7).</summary>
    public const double FeedFloor = 4.5;

    public const int MaxPlaceContainsLength = 100;
}

/// <summary>A point and a radius on the Earth's surface.</summary>
public sealed record GeoCircle(double Latitude, double Longitude, double RadiusKm)
{
    public const double MinRadiusKm = 1;

    /// <summary>
    /// Upper limit of a rule radius. Half the circumference is about 20 015 km, so a 20 000 km circle covers all of
    /// the Earth except about 15 km around the antipode of its centre.
    /// </summary>
    public const double MaxRadiusKm = 20_000;

    /// <summary>Mean Earth radius (IUGG), the usual value for the haversine formula.</summary>
    public const double EarthRadiusKm = 6371.0088;

    /// <summary>Great-circle distance in km (haversine).</summary>
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        static double Rad(double degrees) => degrees * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    public bool Contains(double latitude, double longitude) =>
        DistanceKm(Latitude, Longitude, latitude, longitude) <= RadiusKm;
}
