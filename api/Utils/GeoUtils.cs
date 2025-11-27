namespace Tempo.Api.Utils;

/// <summary>
/// Utility class for geographic calculations.
/// </summary>
public static class GeoUtils
{
    /// <summary>
    /// Calculates the distance between two points on Earth using the Haversine formula.
    /// </summary>
    /// <param name="lat1">Latitude of the first point in degrees</param>
    /// <param name="lon1">Longitude of the first point in degrees</param>
    /// <param name="lat2">Latitude of the second point in degrees</param>
    /// <param name="lon2">Longitude of the second point in degrees</param>
    /// <returns>Distance in meters</returns>
    public static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000; // Earth radius in meters
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    /// <summary>
    /// Converts degrees to radians.
    /// </summary>
    private static double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }
}

