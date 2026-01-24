using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Service for calculating and managing running insights including data coverage,
/// weather extremes, performance highlights, and habit patterns.
/// </summary>
public class InsightsService
{
    private readonly ILogger<InsightsService> _logger;

    public InsightsService(ILogger<InsightsService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Get comprehensive insights about a user's running data.
    /// Returns data coverage metadata and (in future) specific insights.
    /// </summary>
    /// <param name="db">Database context</param>
    /// <returns>Insights response with metadata and statistics</returns>
    public async Task<InsightsResponse> GetInsightsAsync(TempoDbContext db)
    {
        try
        {
            // Get total workout count
            var totalWorkouts = await db.Workouts
                .AsNoTracking()
                .CountAsync();

            // Check if user has sufficient workouts for insights
            if (totalWorkouts < InsightsThresholds.MINIMUM_WORKOUTS)
            {
                return new InsightsResponse
                {
                    Message = $"Complete at least {InsightsThresholds.MINIMUM_WORKOUTS} runs to see insights!",
                    SufficientData = false,
                    CurrentWorkouts = totalWorkouts,
                    RequiredWorkouts = InsightsThresholds.MINIMUM_WORKOUTS,
                    Metadata = new DataCoverageMetadata
                    {
                        TotalWorkouts = totalWorkouts,
                        MinimumWorkoutsRequired = InsightsThresholds.MINIMUM_WORKOUTS,
                        DataAvailability = new Dictionary<string, DataAvailabilityCategory>()
                    }
                };
            }

            // Calculate data coverage metadata
            var metadata = await CalculateDataCoverageAsync(db, totalWorkouts);

            // Calculate weather extremes if sufficient data exists
            WeatherInsights? weatherInsights = null;
            if (metadata.DataAvailability["weather"].Available)
            {
                var userSettings = await db.UserSettings.FirstOrDefaultAsync();
                var unitPreference = userSettings?.UnitPreference ?? "metric";
                weatherInsights = await CalculateWeatherExtremesAsync(db, unitPreference);
            }

            return new InsightsResponse
            {
                SufficientData = true,
                Metadata = metadata,
                Weather = weatherInsights
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating insights");
            throw;
        }
    }

    /// <summary>
    /// Calculate comprehensive data coverage metadata including availability by category.
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="totalWorkouts">Total number of workouts</param>
    /// <returns>Data coverage metadata</returns>
    private async Task<DataCoverageMetadata> CalculateDataCoverageAsync(TempoDbContext db, int totalWorkouts)
    {
        // Execute all data availability queries sequentially (DbContext doesn't support concurrent operations)
        var weatherCount = await db.Workouts
            .AsNoTracking()
            .Where(w => w.Weather != null)
            .CountAsync();

        var heartRateCount = await db.Workouts
            .AsNoTracking()
            .Where(w => w.MaxHeartRateBpm.HasValue)
            .CountAsync();

        var elevationCount = await db.Workouts
            .AsNoTracking()
            .Where(w => w.ElevGainM.HasValue)
            .CountAsync();

        var caloriesCount = await db.Workouts
            .AsNoTracking()
            .Where(w => w.Calories.HasValue)
            .CountAsync();

        var cadenceCount = await db.Workouts
            .AsNoTracking()
            .Where(w => w.MaxCadenceRpm.HasValue)
            .CountAsync();

        var powerCount = await db.Workouts
            .AsNoTracking()
            .Where(w => w.MaxPowerWatts.HasValue)
            .CountAsync();

        var firstWorkout = await db.Workouts
            .AsNoTracking()
            .OrderBy(w => w.StartedAt)
            .FirstOrDefaultAsync();

        var latestWorkout = await db.Workouts
            .AsNoTracking()
            .OrderByDescending(w => w.StartedAt)
            .FirstOrDefaultAsync();

        // Calculate date range
        DateTime? firstWorkoutDate = firstWorkout?.StartedAt;
        DateTime? latestWorkoutDate = latestWorkout?.StartedAt;
        int? daysSinceFirstWorkout = null;

        if (firstWorkoutDate.HasValue && latestWorkoutDate.HasValue)
        {
            daysSinceFirstWorkout = (int)(latestWorkoutDate.Value - firstWorkoutDate.Value).TotalDays;
        }

        // Build data availability dictionary
        var dataAvailability = new Dictionary<string, DataAvailabilityCategory>
        {
            ["weather"] = CreateAvailabilityCategory(weatherCount, totalWorkouts, InsightsThresholds.MINIMUM_WEATHER_COVERAGE, InsightsThresholds.MINIMUM_FOR_WEATHER_STATS),
            ["heartRate"] = CreateAvailabilityCategory(heartRateCount, totalWorkouts, InsightsThresholds.MINIMUM_HR_COVERAGE),
            ["elevation"] = CreateAvailabilityCategory(elevationCount, totalWorkouts, 0.0), // Always show if any data exists
            ["calories"] = CreateAvailabilityCategory(caloriesCount, totalWorkouts, 0.0),
            ["cadence"] = CreateAvailabilityCategory(cadenceCount, totalWorkouts, 0.0),
            ["power"] = CreateAvailabilityCategory(powerCount, totalWorkouts, 0.0)
        };

        return new DataCoverageMetadata
        {
            TotalWorkouts = totalWorkouts,
            FirstWorkoutDate = firstWorkoutDate,
            LatestWorkoutDate = latestWorkoutDate,
            DaysSinceFirstWorkout = daysSinceFirstWorkout,
            DataAvailability = dataAvailability,
            MinimumWorkoutsRequired = InsightsThresholds.MINIMUM_WORKOUTS
        };
    }

    /// <summary>
    /// Create a data availability category with count, percentage, and availability flag.
    /// </summary>
    /// <param name="count">Number of workouts with this data</param>
    /// <param name="totalWorkouts">Total number of workouts</param>
    /// <param name="minimumCoverage">Minimum coverage percentage (0.0-1.0) required for availability</param>
    /// <param name="minimumCount">Minimum absolute count required for availability (default: 1)</param>
    /// <returns>Data availability category</returns>
    private DataAvailabilityCategory CreateAvailabilityCategory(int count, int totalWorkouts, double minimumCoverage, int minimumCount = 1)
    {
        var percentage = totalWorkouts > 0 ? (count * 100.0 / totalWorkouts) : 0.0;
        var coverageRatio = totalWorkouts > 0 ? (count / (double)totalWorkouts) : 0.0;
        var available = count >= minimumCount && coverageRatio >= minimumCoverage;

        return new DataAvailabilityCategory
        {
            Count = count,
            Percentage = Math.Round(percentage, 1),
            Available = available
        };
    }

    /// <summary>
    /// Simple DTO for workout weather data.
    /// </summary>
    private class WorkoutWeatherData
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public DateTime StartedAt { get; set; }
        public string Weather { get; set; } = string.Empty;
    }

    /// <summary>
    /// Calculate weather extremes from workouts with weather data.
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="unitPreference">User unit preference ("metric" or "imperial")</param>
    /// <returns>Weather insights with extreme statistics</returns>
    private async Task<WeatherInsights?> CalculateWeatherExtremesAsync(TempoDbContext db, string unitPreference)
    {
        try
        {
            // Fetch all workouts with weather data (we need to parse JSON in memory)
            var workoutsWithWeather = await db.Workouts
                .AsNoTracking()
                .Where(w => w.Weather != null)
                .Select(w => new WorkoutWeatherData 
                { 
                    Id = w.Id, 
                    Name = w.Name, 
                    StartedAt = w.StartedAt, 
                    Weather = w.Weather!
                })
                .ToListAsync();

            if (workoutsWithWeather.Count < InsightsThresholds.MINIMUM_FOR_WEATHER_STATS)
            {
                return null;
            }

            // Parse weather data and find extremes
            var coldest = FindColdestRun(workoutsWithWeather, unitPreference);
            var hottest = FindHottestRun(workoutsWithWeather, unitPreference);
            var windiest = FindWindiestRun(workoutsWithWeather, unitPreference);
            var mostHumid = FindMostHumidRun(workoutsWithWeather);
            var wettest = FindWettestRun(workoutsWithWeather);
            var mostEpic = FindMostEpicWeatherRun(workoutsWithWeather);
            var foggiest = FindFoggiestRun(workoutsWithWeather);
            var snowiest = FindSnowiestRun(workoutsWithWeather);

            return new WeatherInsights
            {
                Coldest = coldest,
                Hottest = hottest,
                Windiest = windiest,
                MostHumid = mostHumid,
                Wettest = wettest,
                MostEpic = mostEpic,
                Foggiest = foggiest,
                Snowiest = snowiest
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating weather extremes");
            return null; // Fail gracefully
        }
    }

    /// <summary>
    /// Find the coldest run by temperature.
    /// </summary>
    private WeatherExtremeRun? FindColdestRun(
        List<WorkoutWeatherData> workouts,
        string unitPreference)
    {
        WeatherExtremeRun? coldest = null;
        double? minTemp = null;

        foreach (var workout in workouts)
        {
            try
            {
                using var weatherDoc = JsonDocument.Parse((string)workout.Weather);
                if (weatherDoc.RootElement.TryGetProperty("temperature", out var tempElem) &&
                    tempElem.ValueKind == JsonValueKind.Number)
                {
                    var tempC = tempElem.GetDouble();
                    if (!minTemp.HasValue || tempC < minTemp.Value)
                    {
                        minTemp = tempC;
                        coldest = new WeatherExtremeRun
                        {
                            Temperature = Math.Round(UnitConversionService.ConvertTemperature(tempC, unitPreference), 1),
                            TemperatureUnit = UnitConversionService.GetTemperatureUnit(unitPreference),
                            Date = workout.StartedAt,
                            WorkoutId = workout.Id,
                            WorkoutName = workout.Name
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse weather data for workout {WorkoutId}", (Guid)workout.Id);
            }
        }

        return coldest;
    }

    /// <summary>
    /// Find the hottest run by temperature.
    /// </summary>
    private WeatherExtremeRun? FindHottestRun(
        List<WorkoutWeatherData> workouts,
        string unitPreference)
    {
        WeatherExtremeRun? hottest = null;
        double? maxTemp = null;

        foreach (var workout in workouts)
        {
            try
            {
                using var weatherDoc = JsonDocument.Parse((string)workout.Weather);
                if (weatherDoc.RootElement.TryGetProperty("temperature", out var tempElem) &&
                    tempElem.ValueKind == JsonValueKind.Number)
                {
                    var tempC = tempElem.GetDouble();
                    if (!maxTemp.HasValue || tempC > maxTemp.Value)
                    {
                        maxTemp = tempC;
                        hottest = new WeatherExtremeRun
                        {
                            Temperature = Math.Round(UnitConversionService.ConvertTemperature(tempC, unitPreference), 1),
                            TemperatureUnit = UnitConversionService.GetTemperatureUnit(unitPreference),
                            Date = workout.StartedAt,
                            WorkoutId = workout.Id,
                            WorkoutName = workout.Name
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse weather data for workout {WorkoutId}", (Guid)workout.Id);
            }
        }

        return hottest;
    }

    /// <summary>
    /// Find the windiest run by wind speed.
    /// </summary>
    private WindExtremeRun? FindWindiestRun(
        List<WorkoutWeatherData> workouts,
        string unitPreference)
    {
        WindExtremeRun? windiest = null;
        double? maxWindSpeed = null;

        foreach (var workout in workouts)
        {
            try
            {
                using var weatherDoc = JsonDocument.Parse((string)workout.Weather);
                if (weatherDoc.RootElement.TryGetProperty("windSpeed", out var windSpeedElem) &&
                    windSpeedElem.ValueKind == JsonValueKind.Number)
                {
                    var windSpeedMs = windSpeedElem.GetDouble();
                    if (!maxWindSpeed.HasValue || windSpeedMs > maxWindSpeed.Value)
                    {
                        maxWindSpeed = windSpeedMs;

                        // Get wind direction if available
                        int? windDirection = null;
                        string? windDirectionCardinal = null;
                        if (weatherDoc.RootElement.TryGetProperty("windDirection", out var windDirElem) &&
                            windDirElem.ValueKind == JsonValueKind.Number)
                        {
                            windDirection = windDirElem.GetInt32();
                            windDirectionCardinal = UnitConversionService.DegreesToCardinal(windDirection.Value);
                        }

                        windiest = new WindExtremeRun
                        {
                            WindSpeed = Math.Round(UnitConversionService.ConvertWindSpeed(windSpeedMs, unitPreference), 1),
                            WindSpeedUnit = UnitConversionService.GetWindSpeedUnit(unitPreference),
                            WindDirection = windDirection,
                            WindDirectionCardinal = windDirectionCardinal,
                            Date = workout.StartedAt,
                            WorkoutId = workout.Id,
                            WorkoutName = workout.Name
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse weather data for workout {WorkoutId}", (Guid)workout.Id);
            }
        }

        return windiest;
    }

    /// <summary>
    /// Find the most humid run by humidity percentage.
    /// </summary>
    private HumidityExtremeRun? FindMostHumidRun(List<WorkoutWeatherData> workouts)
    {
        HumidityExtremeRun? mostHumid = null;
        double? maxHumidity = null;

        foreach (var workout in workouts)
        {
            try
            {
                using var weatherDoc = JsonDocument.Parse((string)workout.Weather);
                if (weatherDoc.RootElement.TryGetProperty("humidity", out var humidityElem) &&
                    humidityElem.ValueKind == JsonValueKind.Number)
                {
                    var humidity = humidityElem.GetDouble();
                    if (!maxHumidity.HasValue || humidity > maxHumidity.Value)
                    {
                        maxHumidity = humidity;
                        mostHumid = new HumidityExtremeRun
                        {
                            Humidity = Math.Round(humidity, 1),
                            Date = workout.StartedAt,
                            WorkoutId = workout.Id,
                            WorkoutName = workout.Name
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse weather data for workout {WorkoutId}", (Guid)workout.Id);
            }
        }

        return mostHumid;
    }

    /// <summary>
    /// Find the wettest run by precipitation amount.
    /// </summary>
    private PrecipitationExtremeRun? FindWettestRun(List<WorkoutWeatherData> workouts)
    {
        PrecipitationExtremeRun? wettest = null;
        double? maxPrecipitation = null;

        foreach (var workout in workouts)
        {
            try
            {
                using var weatherDoc = JsonDocument.Parse((string)workout.Weather);
                if (weatherDoc.RootElement.TryGetProperty("precipitation", out var precipElem) &&
                    precipElem.ValueKind == JsonValueKind.Number)
                {
                    var precipitation = precipElem.GetDouble();
                    if (precipitation > 0 && (!maxPrecipitation.HasValue || precipitation > maxPrecipitation.Value))
                    {
                        maxPrecipitation = precipitation;
                        wettest = new PrecipitationExtremeRun
                        {
                            Precipitation = Math.Round(precipitation, 1),
                            PrecipitationUnit = "mm",
                            Date = workout.StartedAt,
                            WorkoutId = workout.Id,
                            WorkoutName = workout.Name
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse weather data for workout {WorkoutId}", (Guid)workout.Id);
            }
        }

        return wettest;
    }

    /// <summary>
    /// Find the most epic weather run (thunderstorms - codes 95-99).
    /// Returns the workout with the most severe thunderstorm code.
    /// </summary>
    private EpicWeatherRun? FindMostEpicWeatherRun(List<WorkoutWeatherData> workouts)
    {
        EpicWeatherRun? mostEpic = null;
        int? maxWeatherCode = null;

        foreach (var workout in workouts)
        {
            try
            {
                using var weatherDoc = JsonDocument.Parse((string)workout.Weather);
                if (weatherDoc.RootElement.TryGetProperty("weatherCode", out var codeElem) &&
                    codeElem.ValueKind == JsonValueKind.Number)
                {
                    var weatherCode = codeElem.GetInt32();
                    // Thunderstorm codes: 95-99
                    if (weatherCode >= 95 && weatherCode <= 99)
                    {
                        if (!maxWeatherCode.HasValue || weatherCode > maxWeatherCode.Value)
                        {
                            maxWeatherCode = weatherCode;
                            
                            // Get condition string if available
                            var condition = weatherDoc.RootElement.TryGetProperty("condition", out var condElem) &&
                                          condElem.ValueKind == JsonValueKind.String
                                ? condElem.GetString() ?? WeatherService.MapWeatherCodeToCondition(weatherCode)
                                : WeatherService.MapWeatherCodeToCondition(weatherCode);

                            mostEpic = new EpicWeatherRun
                            {
                                WeatherCode = weatherCode,
                                Condition = condition,
                                Date = workout.StartedAt,
                                WorkoutId = workout.Id,
                                WorkoutName = workout.Name
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse weather data for workout {WorkoutId}", (Guid)workout.Id);
            }
        }

        return mostEpic;
    }

    /// <summary>
    /// Find the foggiest run (fog codes 45-48).
    /// </summary>
    private FoggyWeatherRun? FindFoggiestRun(List<WorkoutWeatherData> workouts)
    {
        FoggyWeatherRun? foggiest = null;
        int? maxFogCode = null;

        foreach (var workout in workouts)
        {
            try
            {
                using var weatherDoc = JsonDocument.Parse((string)workout.Weather);
                if (weatherDoc.RootElement.TryGetProperty("weatherCode", out var codeElem) &&
                    codeElem.ValueKind == JsonValueKind.Number)
                {
                    var weatherCode = codeElem.GetInt32();
                    // Fog codes: 45-48
                    if (weatherCode >= 45 && weatherCode <= 48)
                    {
                        if (!maxFogCode.HasValue || weatherCode > maxFogCode.Value)
                        {
                            maxFogCode = weatherCode;
                            
                            // Get condition string if available
                            var condition = weatherDoc.RootElement.TryGetProperty("condition", out var condElem) &&
                                          condElem.ValueKind == JsonValueKind.String
                                ? condElem.GetString() ?? WeatherService.MapWeatherCodeToCondition(weatherCode)
                                : WeatherService.MapWeatherCodeToCondition(weatherCode);

                            foggiest = new FoggyWeatherRun
                            {
                                WeatherCode = weatherCode,
                                Condition = condition,
                                Date = workout.StartedAt,
                                WorkoutId = workout.Id,
                                WorkoutName = workout.Name
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse weather data for workout {WorkoutId}", (Guid)workout.Id);
            }
        }

        return foggiest;
    }

    /// <summary>
    /// Find the snowiest run (snow codes 71-77, 85-86).
    /// Prioritizes by precipitation amount if available.
    /// </summary>
    private SnowyWeatherRun? FindSnowiestRun(List<WorkoutWeatherData> workouts)
    {
        SnowyWeatherRun? snowiest = null;
        double? maxPrecipitation = null;
        int? fallbackSnowCode = null;

        foreach (var workout in workouts)
        {
            try
            {
                using var weatherDoc = JsonDocument.Parse((string)workout.Weather);
                if (weatherDoc.RootElement.TryGetProperty("weatherCode", out var codeElem) &&
                    codeElem.ValueKind == JsonValueKind.Number)
                {
                    var weatherCode = codeElem.GetInt32();
                    // Snow codes: 71-77, 85-86
                    if ((weatherCode >= 71 && weatherCode <= 77) || (weatherCode >= 85 && weatherCode <= 86))
                    {
                        // Try to get precipitation amount
                        double? precipitation = null;
                        if (weatherDoc.RootElement.TryGetProperty("precipitation", out var precipElem) &&
                            precipElem.ValueKind == JsonValueKind.Number)
                        {
                            precipitation = precipElem.GetDouble();
                        }

                        // Prioritize by precipitation if available
                        bool isNewMax = false;
                        if (precipitation.HasValue && precipitation.Value > 0)
                        {
                            if (!maxPrecipitation.HasValue || precipitation.Value > maxPrecipitation.Value)
                            {
                                maxPrecipitation = precipitation.Value;
                                isNewMax = true;
                            }
                        }
                        else if (!maxPrecipitation.HasValue && !fallbackSnowCode.HasValue)
                        {
                            // No precipitation data available, just pick first snow run
                            fallbackSnowCode = weatherCode;
                            isNewMax = true;
                        }

                        if (isNewMax)
                        {
                            // Get condition string if available
                            var condition = weatherDoc.RootElement.TryGetProperty("condition", out var condElem) &&
                                          condElem.ValueKind == JsonValueKind.String
                                ? condElem.GetString() ?? WeatherService.MapWeatherCodeToCondition(weatherCode)
                                : WeatherService.MapWeatherCodeToCondition(weatherCode);

                            snowiest = new SnowyWeatherRun
                            {
                                WeatherCode = weatherCode,
                                Condition = condition,
                                Precipitation = precipitation,
                                Date = workout.StartedAt,
                                WorkoutId = workout.Id,
                                WorkoutName = workout.Name
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse weather data for workout {WorkoutId}", (Guid)workout.Id);
            }
        }

        return snowiest;
    }
}
