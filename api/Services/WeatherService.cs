using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tempo.Api.Services;

public class WeatherService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WeatherService> _logger;
    private const string OpenMeteoArchiveUrl = "https://archive-api.open-meteo.com/v1/archive";

    public WeatherService(HttpClient httpClient, ILogger<WeatherService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Extracts weather data from Strava CSV RawStravaData JSON
    /// </summary>
    public Dictionary<string, object>? ExtractWeatherFromStravaData(string? rawStravaDataJson)
    {
        if (string.IsNullOrEmpty(rawStravaDataJson))
        {
            return null;
        }

        try
        {
            var rawData = JsonSerializer.Deserialize<JsonElement>(rawStravaDataJson);
            if (rawData.TryGetProperty("weather", out var weatherElement) && weatherElement.ValueKind == JsonValueKind.Object)
            {
                var weather = new Dictionary<string, object>();
                foreach (var prop in weatherElement.EnumerateObject())
                {
                    weather[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString() ?? (object)string.Empty,
                        JsonValueKind.Number => prop.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null!,
                        _ => prop.Value.GetRawText()
                    };
                }

                if (weather.Count > 0)
                {
                    weather["source"] = "strava_import";
                    weather["fetchedAt"] = DateTime.UtcNow.ToString("O");
                    
                    // Map Strava condition string to WMO weather code if condition exists but weatherCode doesn't
                    if (weather.ContainsKey("condition") && !weather.ContainsKey("weatherCode"))
                    {
                        var condition = weather["condition"]?.ToString();
                        if (!string.IsNullOrEmpty(condition))
                        {
                            var weatherCode = MapStravaConditionToWeatherCode(condition);
                            if (weatherCode.HasValue)
                            {
                                weather["weatherCode"] = weatherCode.Value;
                            }
                        }
                    }
                    
                    // Convert numeric condition to human-readable string
                    if (weather.ContainsKey("condition"))
                    {
                        var condition = weather["condition"];
                        // Check if condition is numeric (could be stored as double or string representation of number)
                        int? numericCode = null;
                        if (condition is double dblCondition)
                        {
                            numericCode = (int)Math.Round(dblCondition);
                        }
                        else if (condition is int intCondition)
                        {
                            numericCode = intCondition;
                        }
                        else if (condition is string strCondition && !string.IsNullOrWhiteSpace(strCondition))
                        {
                            // Try parsing string as numeric code
                            if (int.TryParse(strCondition.Trim(), out var parsedCode))
                            {
                                numericCode = parsedCode;
                            }
                        }
                        
                        // If condition is numeric, convert to human-readable string
                        if (numericCode.HasValue && numericCode.Value >= 0 && numericCode.Value <= 99)
                        {
                            weather["condition"] = MapWeatherCodeToCondition(numericCode.Value);
                            // Also ensure weatherCode is set if it wasn't already
                            if (!weather.ContainsKey("weatherCode"))
                            {
                                weather["weatherCode"] = numericCode.Value;
                            }
                        }
                    }
                    
                    // If weatherCode exists but condition doesn't, set condition from weatherCode
                    if (weather.ContainsKey("weatherCode") && !weather.ContainsKey("condition"))
                    {
                        var weatherCode = weather["weatherCode"];
                        int? code = null;
                        if (weatherCode is double dblCode)
                        {
                            code = (int)Math.Round(dblCode);
                        }
                        else if (weatherCode is int intCode)
                        {
                            code = intCode;
                        }
                        
                        if (code.HasValue)
                        {
                            weather["condition"] = MapWeatherCodeToCondition(code.Value);
                        }
                    }
                    
                    // Normalize humidity value to ensure it's in 0-100 range (percentage)
                    if (weather.ContainsKey("humidity"))
                    {
                        weather["humidity"] = NormalizeHumidityValue(weather["humidity"]);
                    }
                    
                    return weather;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract weather from Strava data");
        }

        return null;
    }

    /// <summary>
    /// Extracts weather data from FIT file RawFitData JSON
    /// </summary>
    public Dictionary<string, object>? ExtractWeatherFromFitData(string? rawFitDataJson)
    {
        if (string.IsNullOrEmpty(rawFitDataJson))
        {
            return null;
        }

        try
        {
            var rawData = JsonSerializer.Deserialize<JsonElement>(rawFitDataJson);
            if (rawData.TryGetProperty("weather", out var weatherElement) && weatherElement.ValueKind == JsonValueKind.Object)
            {
                var weather = new Dictionary<string, object>();
                foreach (var prop in weatherElement.EnumerateObject())
                {
                    weather[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString() ?? (object)string.Empty,
                        JsonValueKind.Number => prop.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null!,
                        _ => prop.Value.GetRawText()
                    };
                }

                if (weather.Count > 0)
                {
                    weather["source"] = "fit_import";
                    weather["fetchedAt"] = DateTime.UtcNow.ToString("O");
                    
                    // Normalize relativeHumidity to humidity (FIT files use relativeHumidity)
                    if (weather.ContainsKey("relativeHumidity") && !weather.ContainsKey("humidity"))
                    {
                        var relativeHumidity = weather["relativeHumidity"];
                        weather["humidity"] = NormalizeHumidityValue(relativeHumidity);
                        weather.Remove("relativeHumidity");
                    }
                    // If humidity already exists, normalize it
                    else if (weather.ContainsKey("humidity"))
                    {
                        weather["humidity"] = NormalizeHumidityValue(weather["humidity"]);
                    }
                    
                    return weather;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract weather from FIT data");
        }

        return null;
    }

    /// <summary>
    /// Fetches historical weather data from Open-Meteo API
    /// </summary>
    public async Task<Dictionary<string, object>?> FetchWeatherFromOpenMeteoAsync(
        double latitude,
        double longitude,
        DateTime startTime)
    {
        try
        {
            // Format date as YYYY-MM-DD
            var dateStr = startTime.ToString("yyyy-MM-dd");
            
            // Build query parameters
            var queryParams = new List<string>
            {
                $"latitude={latitude:F6}",
                $"longitude={longitude:F6}",
                $"start_date={dateStr}",
                $"end_date={dateStr}",
                "hourly=temperature_2m,relative_humidity_2m,precipitation,weather_code,wind_speed_10m,wind_direction_10m,surface_pressure"
            };

            var url = $"{OpenMeteoArchiveUrl}?{string.Join("&", queryParams)}";
            
            _logger.LogInformation("Fetching weather from Open-Meteo: {Url}", url);

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var responseData = JsonSerializer.Deserialize<JsonElement>(jsonResponse);

            if (!responseData.TryGetProperty("hourly", out var hourlyElement))
            {
                _logger.LogWarning("Open-Meteo response missing hourly data");
                return null;
            }

            // Get the time array and find the closest hour to workout start time
            if (!hourlyElement.TryGetProperty("time", out var timeArray) || timeArray.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("Open-Meteo response missing time array");
                return null;
            }

            // Find the hour index closest to workout start time
            int? closestHourIndex = null;
            var workoutHour = startTime.Hour;
            var minTimeDiff = double.MaxValue;

            for (int i = 0; i < timeArray.GetArrayLength(); i++)
            {
                var timeStr = timeArray[i].GetString();
                if (DateTime.TryParse(timeStr, out var time))
                {
                    var timeDiff = Math.Abs((time - startTime).TotalHours);
                    if (timeDiff < minTimeDiff)
                    {
                        minTimeDiff = timeDiff;
                        closestHourIndex = i;
                    }
                }
            }

            if (!closestHourIndex.HasValue)
            {
                _logger.LogWarning("Could not find matching hour in Open-Meteo response");
                return null;
            }

            var idx = closestHourIndex.Value;

            // Extract weather data at the selected hour
            var weather = new Dictionary<string, object>
            {
                ["source"] = "open_meteo",
                ["fetchedAt"] = DateTime.UtcNow.ToString("O")
            };

            // Extract temperature
            if (hourlyElement.TryGetProperty("temperature_2m", out var tempArray) && 
                tempArray.ValueKind == JsonValueKind.Array && 
                idx < tempArray.GetArrayLength())
            {
                var temp = tempArray[idx];
                if (temp.ValueKind == JsonValueKind.Number)
                {
                    weather["temperature"] = temp.GetDouble();
                }
            }

            // Extract humidity
            if (hourlyElement.TryGetProperty("relative_humidity_2m", out var humidityArray) && 
                humidityArray.ValueKind == JsonValueKind.Array && 
                idx < humidityArray.GetArrayLength())
            {
                var humidity = humidityArray[idx];
                if (humidity.ValueKind == JsonValueKind.Number)
                {
                    weather["humidity"] = NormalizeHumidityValue(humidity.GetDouble());
                }
            }

            // Extract precipitation
            if (hourlyElement.TryGetProperty("precipitation", out var precipArray) && 
                precipArray.ValueKind == JsonValueKind.Array && 
                idx < precipArray.GetArrayLength())
            {
                var precip = precipArray[idx];
                if (precip.ValueKind == JsonValueKind.Number)
                {
                    weather["precipitation"] = precip.GetDouble();
                }
            }

            // Extract weather code (WMO weather code)
            if (hourlyElement.TryGetProperty("weather_code", out var codeArray) && 
                codeArray.ValueKind == JsonValueKind.Array && 
                idx < codeArray.GetArrayLength())
            {
                var code = codeArray[idx];
                if (code.ValueKind == JsonValueKind.Number)
                {
                    weather["weatherCode"] = code.GetInt32();
                    // Map WMO weather code to condition string
                    weather["condition"] = MapWeatherCodeToCondition(code.GetInt32());
                }
            }

            // Extract wind speed
            if (hourlyElement.TryGetProperty("wind_speed_10m", out var windSpeedArray) && 
                windSpeedArray.ValueKind == JsonValueKind.Array && 
                idx < windSpeedArray.GetArrayLength())
            {
                var windSpeed = windSpeedArray[idx];
                if (windSpeed.ValueKind == JsonValueKind.Number)
                {
                    weather["windSpeed"] = windSpeed.GetDouble();
                }
            }

            // Extract wind direction
            if (hourlyElement.TryGetProperty("wind_direction_10m", out var windDirArray) && 
                windDirArray.ValueKind == JsonValueKind.Array && 
                idx < windDirArray.GetArrayLength())
            {
                var windDir = windDirArray[idx];
                if (windDir.ValueKind == JsonValueKind.Number)
                {
                    weather["windDirection"] = windDir.GetInt32();
                }
            }

            // Extract pressure
            if (hourlyElement.TryGetProperty("surface_pressure", out var pressureArray) && 
                pressureArray.ValueKind == JsonValueKind.Array && 
                idx < pressureArray.GetArrayLength())
            {
                var pressure = pressureArray[idx];
                if (pressure.ValueKind == JsonValueKind.Number)
                {
                    weather["pressure"] = pressure.GetDouble();
                }
            }

            return weather.Count > 2 ? weather : null; // More than just source and fetchedAt
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch weather from Open-Meteo API");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error fetching weather from Open-Meteo API");
            return null;
        }
    }

    /// <summary>
    /// Main method to get weather for a workout - tries import file weather first, then Open-Meteo
    /// </summary>
    public async Task<string?> GetWeatherForWorkoutAsync(
        string? rawStravaDataJson,
        string? rawFitDataJson,
        double? latitude,
        double? longitude,
        DateTime startTime)
    {
        // Try Strava data first
        var weather = ExtractWeatherFromStravaData(rawStravaDataJson);
        if (weather != null)
        {
            return JsonSerializer.Serialize(weather, new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
        }

        // Try FIT data
        weather = ExtractWeatherFromFitData(rawFitDataJson);
        if (weather != null)
        {
            return JsonSerializer.Serialize(weather, new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
        }

        // Fallback to Open-Meteo if we have coordinates
        if (latitude.HasValue && longitude.HasValue)
        {
            weather = await FetchWeatherFromOpenMeteoAsync(latitude.Value, longitude.Value, startTime);
            if (weather != null)
            {
                return JsonSerializer.Serialize(weather, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });
            }
        }
        else
        {
            _logger.LogDebug("Skipping weather fetch - no GPS coordinates available (indoor activity)");
        }

        return null;
    }

    /// <summary>
    /// Maps Strava condition string or numeric code to WMO weather code
    /// </summary>
    private static int? MapStravaConditionToWeatherCode(string condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return null;
        }

        // Try parsing as numeric code first (Strava may use numeric codes)
        if (double.TryParse(condition.Trim(), out var numericCode))
        {
            // Map Strava numeric codes to WMO codes if they differ
            // If Strava uses WMO codes directly, return as-is (clamped to valid range)
            var code = (int)Math.Round(numericCode);
            if (code >= 0 && code <= 99)
            {
                return code;
            }
        }

        // Normalize condition string (lowercase, trim)
        var normalized = condition.Trim().ToLowerInvariant();

        // Map common Strava condition strings to WMO weather codes
        return normalized switch
        {
            // Clear/Sunny conditions
            "clear" or "sunny" or "fair" => 0,
            
            // Partly cloudy
            "partly cloudy" or "partlycloudy" or "mostly sunny" or "mostlysunny" => 1,
            
            // Cloudy/Overcast
            "cloudy" or "overcast" or "mostly cloudy" or "mostlycloudy" => 3,
            
            // Fog/Mist
            "fog" or "foggy" or "mist" or "haze" => 45,
            
            // Drizzle
            "drizzle" or "light drizzle" or "lightdrizzle" => 51,
            "moderate drizzle" or "moderatedrizzle" => 53,
            "heavy drizzle" or "heavydrizzle" or "dense drizzle" or "densedrizzle" => 55,
            
            // Rain
            "rain" or "light rain" or "lightrain" or "slight rain" or "slightrain" => 61,
            "moderate rain" or "moderaterain" => 63,
            "heavy rain" or "heavyrain" => 65,
            
            // Freezing rain
            "freezing rain" or "freezingrain" or "light freezing rain" or "lightfreezingrain" => 66,
            "heavy freezing rain" or "heavyfreezingrain" => 67,
            
            // Snow
            "snow" or "light snow" or "lightsnow" or "slight snow" or "slightsnow" => 71,
            "moderate snow" or "moderatesnow" => 73,
            "heavy snow" or "heavysnow" => 75,
            "snow grains" or "snowgrains" => 77,
            
            // Rain showers
            "rain showers" or "rainshowers" or "light rain showers" or "lightrainshowers" or "slight rain showers" or "slightrainshowers" => 80,
            "moderate rain showers" or "moderaterainshowers" => 81,
            "heavy rain showers" or "heavyrainshowers" or "violent rain showers" or "violentrainshowers" => 82,
            
            // Snow showers
            "snow showers" or "snowshowers" or "light snow showers" or "lightsnowshowers" or "slight snow showers" or "slightsnowshowers" => 85,
            "heavy snow showers" or "heavysnowshowers" => 86,
            
            // Thunderstorms
            "thunderstorm" or "thunderstorms" or "thunder" => 95,
            "thunderstorm with hail" or "thunderstormwithhail" or "thunderstorm with slight hail" or "thunderstormwithslight hail" => 96,
            "thunderstorm with heavy hail" or "thunderstormwithheavyhail" => 99,
            
            // Default: return null if no match found
            _ => null
        };
    }

    /// <summary>
    /// Normalizes humidity value to percentage range (0-100).
    /// Converts decimal values (0.0-1.0) to percentages (0-100).
    /// </summary>
    public static object NormalizeHumidityValue(object? humidityValue)
    {
        if (humidityValue == null)
        {
            return null!;
        }

        double humidity = 0;
        
        // Extract numeric value
        if (humidityValue is double dbl)
        {
            humidity = dbl;
        }
        else if (humidityValue is int intVal)
        {
            humidity = intVal;
        }
        else if (humidityValue is byte byteVal)
        {
            humidity = byteVal;
        }
        else if (humidityValue is string strVal && double.TryParse(strVal, out var parsed))
        {
            humidity = parsed;
        }
        else
        {
            // Return as-is if we can't parse it
            return humidityValue;
        }

        // If value is between 0.0 and 1.0 (exclusive), it's likely a decimal format
        // Convert to percentage by multiplying by 100
        if (humidity > 0.0 && humidity <= 1.0)
        {
            return humidity * 100.0;
        }

        // If value is already in 0-100 range, return as-is
        // Clamp to valid range (0-100) for safety
        if (humidity < 0)
        {
            return 0.0;
        }
        if (humidity > 100)
        {
            return 100.0;
        }

        return humidity;
    }

    /// <summary>
    /// Maps WMO weather code to human-readable condition string
    /// </summary>
    private static string MapWeatherCodeToCondition(int code)
    {
        // WMO Weather interpretation codes (WW)
        return code switch
        {
            0 => "Clear sky",
            1 => "Mainly clear",
            2 => "Partly cloudy",
            3 => "Overcast",
            45 => "Foggy",
            48 => "Depositing rime fog",
            51 => "Light drizzle",
            53 => "Moderate drizzle",
            55 => "Dense drizzle",
            56 => "Light freezing drizzle",
            57 => "Dense freezing drizzle",
            61 => "Slight rain",
            63 => "Moderate rain",
            65 => "Heavy rain",
            66 => "Light freezing rain",
            67 => "Heavy freezing rain",
            71 => "Slight snow fall",
            73 => "Moderate snow fall",
            75 => "Heavy snow fall",
            77 => "Snow grains",
            80 => "Slight rain showers",
            81 => "Moderate rain showers",
            82 => "Violent rain showers",
            85 => "Slight snow showers",
            86 => "Heavy snow showers",
            95 => "Thunderstorm",
            96 => "Thunderstorm with slight hail",
            99 => "Thunderstorm with heavy hail",
            _ => "Unknown"
        };
    }
}

