namespace Tempo.Api.Services;

/// <summary>
/// Service for converting between different unit systems (metric/imperial).
/// Handles temperature, wind speed, and wind direction conversions.
/// </summary>
public static class UnitConversionService
{
    /// <summary>
    /// Convert temperature from Celsius to Fahrenheit.
    /// </summary>
    /// <param name="celsius">Temperature in Celsius</param>
    /// <returns>Temperature in Fahrenheit</returns>
    public static double CelsiusToFahrenheit(double celsius)
    {
        return (celsius * 9.0 / 5.0) + 32.0;
    }

    /// <summary>
    /// Convert temperature based on user preference.
    /// </summary>
    /// <param name="celsius">Temperature in Celsius (storage format)</param>
    /// <param name="unitPreference">User unit preference ("metric" or "imperial")</param>
    /// <returns>Temperature in user's preferred unit</returns>
    public static double ConvertTemperature(double celsius, string unitPreference)
    {
        if (unitPreference.Equals("imperial", StringComparison.OrdinalIgnoreCase))
        {
            return CelsiusToFahrenheit(celsius);
        }
        return celsius;
    }

    /// <summary>
    /// Convert wind speed from meters per second to kilometers per hour.
    /// </summary>
    /// <param name="metersPerSecond">Wind speed in m/s</param>
    /// <returns>Wind speed in km/h</returns>
    public static double MetersPerSecondToKilometersPerHour(double metersPerSecond)
    {
        return metersPerSecond * 3.6;
    }

    /// <summary>
    /// Convert wind speed from meters per second to miles per hour.
    /// </summary>
    /// <param name="metersPerSecond">Wind speed in m/s</param>
    /// <returns>Wind speed in mph</returns>
    public static double MetersPerSecondToMilesPerHour(double metersPerSecond)
    {
        return metersPerSecond * 2.237;
    }

    /// <summary>
    /// Convert wind speed based on user preference.
    /// </summary>
    /// <param name="metersPerSecond">Wind speed in m/s (storage format)</param>
    /// <param name="unitPreference">User unit preference ("metric" or "imperial")</param>
    /// <returns>Wind speed in user's preferred unit</returns>
    public static double ConvertWindSpeed(double metersPerSecond, string unitPreference)
    {
        if (unitPreference.Equals("imperial", StringComparison.OrdinalIgnoreCase))
        {
            return MetersPerSecondToMilesPerHour(metersPerSecond);
        }
        // Default to m/s for metric (no conversion needed)
        return metersPerSecond;
    }

    /// <summary>
    /// Convert wind direction from degrees to cardinal direction.
    /// </summary>
    /// <param name="degrees">Wind direction in degrees (0-360)</param>
    /// <returns>Cardinal direction (N, NNE, NE, ENE, E, ESE, SE, SSE, S, SSW, SW, WSW, W, WNW, NW, NNW)</returns>
    public static string DegreesToCardinal(int degrees)
    {
        string[] cardinals = 
        { 
            "N", "NNE", "NE", "ENE", 
            "E", "ESE", "SE", "SSE",
            "S", "SSW", "SW", "WSW", 
            "W", "WNW", "NW", "NNW" 
        };
        
        // Normalize degrees to 0-360 range
        int normalizedDegrees = ((degrees % 360) + 360) % 360;
        
        // Each cardinal direction covers 22.5 degrees
        int index = (int)Math.Round(normalizedDegrees / 22.5) % 16;
        return cardinals[index];
    }

    /// <summary>
    /// Get temperature unit string based on user preference.
    /// </summary>
    /// <param name="unitPreference">User unit preference ("metric" or "imperial")</param>
    /// <returns>Temperature unit string (°C or °F)</returns>
    public static string GetTemperatureUnit(string unitPreference)
    {
        return unitPreference.Equals("imperial", StringComparison.OrdinalIgnoreCase) ? "°F" : "°C";
    }

    /// <summary>
    /// Get wind speed unit string based on user preference.
    /// </summary>
    /// <param name="unitPreference">User unit preference ("metric" or "imperial")</param>
    /// <returns>Wind speed unit string (m/s or mph)</returns>
    public static string GetWindSpeedUnit(string unitPreference)
    {
        return unitPreference.Equals("imperial", StringComparison.OrdinalIgnoreCase) ? "mph" : "m/s";
    }
}
