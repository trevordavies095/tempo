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
