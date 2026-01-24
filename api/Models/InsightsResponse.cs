using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Tempo.Api.Models;

/// <summary>
/// Response model for the insights endpoint.
/// Contains metadata about data coverage and (in future stories) actual insight statistics.
/// </summary>
public class InsightsResponse
{
    /// <summary>
    /// Message displayed when user has insufficient workouts for insights.
    /// Null when sufficient data exists.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Indicates whether the user has sufficient workout data to see insights.
    /// </summary>
    [Required]
    [JsonPropertyName("sufficientData")]
    public bool SufficientData { get; set; }

    /// <summary>
    /// Current number of workouts the user has completed.
    /// Only included when insufficient data exists.
    /// </summary>
    [JsonPropertyName("currentWorkouts")]
    public int? CurrentWorkouts { get; set; }

    /// <summary>
    /// Number of workouts required to see insights.
    /// Only included when insufficient data exists.
    /// </summary>
    [JsonPropertyName("requiredWorkouts")]
    public int? RequiredWorkouts { get; set; }

    /// <summary>
    /// Metadata about data coverage and availability.
    /// </summary>
    [Required]
    [JsonPropertyName("metadata")]
    public DataCoverageMetadata Metadata { get; set; } = new();

    /// <summary>
    /// Weather extremes statistics (coldest, hottest, windiest runs, etc.)
    /// Null if insufficient weather data exists.
    /// </summary>
    [JsonPropertyName("weather")]
    public WeatherInsights? Weather { get; set; }
}

/// <summary>
/// Metadata about overall workout data coverage and availability.
/// </summary>
public class DataCoverageMetadata
{
    /// <summary>
    /// Total number of workouts in the database.
    /// </summary>
    [Required]
    [JsonPropertyName("totalWorkouts")]
    public int TotalWorkouts { get; set; }

    /// <summary>
    /// Date and time of the first (earliest) workout.
    /// Null if no workouts exist.
    /// </summary>
    [JsonPropertyName("firstWorkoutDate")]
    public DateTime? FirstWorkoutDate { get; set; }

    /// <summary>
    /// Date and time of the latest (most recent) workout.
    /// Null if no workouts exist.
    /// </summary>
    [JsonPropertyName("latestWorkoutDate")]
    public DateTime? LatestWorkoutDate { get; set; }

    /// <summary>
    /// Number of days between the first and latest workout.
    /// Null if fewer than 2 workouts exist.
    /// </summary>
    [JsonPropertyName("daysSinceFirstWorkout")]
    public int? DaysSinceFirstWorkout { get; set; }

    /// <summary>
    /// Data availability breakdown by category (weather, HR, elevation, etc.).
    /// </summary>
    [Required]
    [JsonPropertyName("dataAvailability")]
    public Dictionary<string, DataAvailabilityCategory> DataAvailability { get; set; } = new();

    /// <summary>
    /// Minimum number of workouts required to see insights.
    /// </summary>
    [Required]
    [JsonPropertyName("minimumWorkoutsRequired")]
    public int MinimumWorkoutsRequired { get; set; }
}

/// <summary>
/// Data availability information for a specific data category.
/// </summary>
public class DataAvailabilityCategory
{
    /// <summary>
    /// Number of workouts that have this type of data.
    /// </summary>
    [Required]
    [JsonPropertyName("count")]
    public int Count { get; set; }

    /// <summary>
    /// Percentage of total workouts that have this type of data (0-100).
    /// </summary>
    [Required]
    [JsonPropertyName("percentage")]
    public double Percentage { get; set; }

    /// <summary>
    /// Whether sufficient data is available to show insights for this category.
    /// Based on count thresholds and minimum coverage percentages.
    /// </summary>
    [Required]
    [JsonPropertyName("available")]
    public bool Available { get; set; }
}

/// <summary>
/// Weather extremes statistics including coldest, hottest, windiest runs and special conditions.
/// </summary>
public class WeatherInsights
{
    /// <summary>
    /// Coldest run by temperature.
    /// </summary>
    [JsonPropertyName("coldest")]
    public WeatherExtremeRun? Coldest { get; set; }

    /// <summary>
    /// Hottest run by temperature.
    /// </summary>
    [JsonPropertyName("hottest")]
    public WeatherExtremeRun? Hottest { get; set; }

    /// <summary>
    /// Windiest run by wind speed.
    /// </summary>
    [JsonPropertyName("windiest")]
    public WindExtremeRun? Windiest { get; set; }

    /// <summary>
    /// Most humid run by humidity percentage.
    /// </summary>
    [JsonPropertyName("mostHumid")]
    public HumidityExtremeRun? MostHumid { get; set; }

    /// <summary>
    /// Wettest run by precipitation amount.
    /// </summary>
    [JsonPropertyName("wettest")]
    public PrecipitationExtremeRun? Wettest { get; set; }

    /// <summary>
    /// Most epic weather run (thunderstorms, severe weather).
    /// </summary>
    [JsonPropertyName("mostEpic")]
    public EpicWeatherRun? MostEpic { get; set; }

    /// <summary>
    /// Foggiest run.
    /// </summary>
    [JsonPropertyName("foggiest")]
    public FoggyWeatherRun? Foggiest { get; set; }

    /// <summary>
    /// Snowiest run.
    /// </summary>
    [JsonPropertyName("snowiest")]
    public SnowyWeatherRun? Snowiest { get; set; }
}

/// <summary>
/// Temperature extreme run (coldest or hottest).
/// </summary>
public class WeatherExtremeRun
{
    /// <summary>
    /// Temperature value in user's preferred unit.
    /// </summary>
    [Required]
    [JsonPropertyName("temperature")]
    public double Temperature { get; set; }

    /// <summary>
    /// Temperature unit (°C or °F).
    /// </summary>
    [Required]
    [JsonPropertyName("temperatureUnit")]
    public string TemperatureUnit { get; set; } = string.Empty;

    /// <summary>
    /// Date and time of the workout.
    /// </summary>
    [Required]
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    /// <summary>
    /// Workout ID for linking to workout details.
    /// </summary>
    [Required]
    [JsonPropertyName("workoutId")]
    public Guid WorkoutId { get; set; }

    /// <summary>
    /// Workout name/title.
    /// </summary>
    [JsonPropertyName("workoutName")]
    public string? WorkoutName { get; set; }
}

/// <summary>
/// Windiest run information.
/// </summary>
public class WindExtremeRun
{
    /// <summary>
    /// Wind speed in user's preferred unit.
    /// </summary>
    [Required]
    [JsonPropertyName("windSpeed")]
    public double WindSpeed { get; set; }

    /// <summary>
    /// Wind speed unit (m/s, km/h, or mph).
    /// </summary>
    [Required]
    [JsonPropertyName("windSpeedUnit")]
    public string WindSpeedUnit { get; set; } = string.Empty;

    /// <summary>
    /// Wind direction in degrees (0-360).
    /// </summary>
    [JsonPropertyName("windDirection")]
    public int? WindDirection { get; set; }

    /// <summary>
    /// Wind direction as cardinal direction (N, NE, E, SE, S, SW, W, NW).
    /// </summary>
    [JsonPropertyName("windDirectionCardinal")]
    public string? WindDirectionCardinal { get; set; }

    /// <summary>
    /// Date and time of the workout.
    /// </summary>
    [Required]
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    /// <summary>
    /// Workout ID for linking to workout details.
    /// </summary>
    [Required]
    [JsonPropertyName("workoutId")]
    public Guid WorkoutId { get; set; }

    /// <summary>
    /// Workout name/title.
    /// </summary>
    [JsonPropertyName("workoutName")]
    public string? WorkoutName { get; set; }
}

/// <summary>
/// Most humid run information.
/// </summary>
public class HumidityExtremeRun
{
    /// <summary>
    /// Humidity percentage (0-100).
    /// </summary>
    [Required]
    [JsonPropertyName("humidity")]
    public double Humidity { get; set; }

    /// <summary>
    /// Date and time of the workout.
    /// </summary>
    [Required]
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    /// <summary>
    /// Workout ID for linking to workout details.
    /// </summary>
    [Required]
    [JsonPropertyName("workoutId")]
    public Guid WorkoutId { get; set; }

    /// <summary>
    /// Workout name/title.
    /// </summary>
    [JsonPropertyName("workoutName")]
    public string? WorkoutName { get; set; }
}

/// <summary>
/// Wettest run information (by precipitation).
/// </summary>
public class PrecipitationExtremeRun
{
    /// <summary>
    /// Precipitation amount in millimeters.
    /// </summary>
    [Required]
    [JsonPropertyName("precipitation")]
    public double Precipitation { get; set; }

    /// <summary>
    /// Precipitation unit (always mm).
    /// </summary>
    [Required]
    [JsonPropertyName("precipitationUnit")]
    public string PrecipitationUnit { get; set; } = "mm";

    /// <summary>
    /// Date and time of the workout.
    /// </summary>
    [Required]
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    /// <summary>
    /// Workout ID for linking to workout details.
    /// </summary>
    [Required]
    [JsonPropertyName("workoutId")]
    public Guid WorkoutId { get; set; }

    /// <summary>
    /// Workout name/title.
    /// </summary>
    [JsonPropertyName("workoutName")]
    public string? WorkoutName { get; set; }
}

/// <summary>
/// Most epic weather run (thunderstorms, severe weather).
/// </summary>
public class EpicWeatherRun
{
    /// <summary>
    /// WMO weather code (95-99 for thunderstorms).
    /// </summary>
    [Required]
    [JsonPropertyName("weatherCode")]
    public int WeatherCode { get; set; }

    /// <summary>
    /// Weather condition description.
    /// </summary>
    [Required]
    [JsonPropertyName("condition")]
    public string Condition { get; set; } = string.Empty;

    /// <summary>
    /// Date and time of the workout.
    /// </summary>
    [Required]
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    /// <summary>
    /// Workout ID for linking to workout details.
    /// </summary>
    [Required]
    [JsonPropertyName("workoutId")]
    public Guid WorkoutId { get; set; }

    /// <summary>
    /// Workout name/title.
    /// </summary>
    [JsonPropertyName("workoutName")]
    public string? WorkoutName { get; set; }
}

/// <summary>
/// Foggiest run information.
/// </summary>
public class FoggyWeatherRun
{
    /// <summary>
    /// WMO weather code (45-48 for fog conditions).
    /// </summary>
    [Required]
    [JsonPropertyName("weatherCode")]
    public int WeatherCode { get; set; }

    /// <summary>
    /// Weather condition description.
    /// </summary>
    [Required]
    [JsonPropertyName("condition")]
    public string Condition { get; set; } = string.Empty;

    /// <summary>
    /// Date and time of the workout.
    /// </summary>
    [Required]
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    /// <summary>
    /// Workout ID for linking to workout details.
    /// </summary>
    [Required]
    [JsonPropertyName("workoutId")]
    public Guid WorkoutId { get; set; }

    /// <summary>
    /// Workout name/title.
    /// </summary>
    [JsonPropertyName("workoutName")]
    public string? WorkoutName { get; set; }
}

/// <summary>
/// Snowiest run information.
/// </summary>
public class SnowyWeatherRun
{
    /// <summary>
    /// WMO weather code (71-77, 85-86 for snow conditions).
    /// </summary>
    [Required]
    [JsonPropertyName("weatherCode")]
    public int WeatherCode { get; set; }

    /// <summary>
    /// Weather condition description.
    /// </summary>
    [Required]
    [JsonPropertyName("condition")]
    public string Condition { get; set; } = string.Empty;

    /// <summary>
    /// Precipitation amount in millimeters (if available).
    /// </summary>
    [JsonPropertyName("precipitation")]
    public double? Precipitation { get; set; }

    /// <summary>
    /// Date and time of the workout.
    /// </summary>
    [Required]
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    /// <summary>
    /// Workout ID for linking to workout details.
    /// </summary>
    [Required]
    [JsonPropertyName("workoutId")]
    public Guid WorkoutId { get; set; }

    /// <summary>
    /// Workout name/title.
    /// </summary>
    [JsonPropertyName("workoutName")]
    public string? WorkoutName { get; set; }
}
