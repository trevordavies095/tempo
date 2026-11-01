using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Tempo.Api.Models.Responses;

/// <summary>
/// One split on <c>GET /workouts/{id}</c>.
/// </summary>
public class WorkoutSplitDocument
{
    [Required]
    [JsonPropertyName("idx")]
    public int Idx { get; set; }

    [Required]
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("distanceM")]
    public double DistanceM { get; set; }

    [Required]
    [JsonPropertyName("durationS")]
    public int DurationS { get; set; }

    [Required]
    [JsonPropertyName("paceS")]
    public double PaceS { get; set; }

    [JsonPropertyName("avgHeartRateBpm")]
    public byte? AvgHeartRateBpm { get; set; }

    [Required]
    [JsonPropertyName("startElapsedS")]
    public int StartElapsedS { get; set; }

    [Required]
    [JsonPropertyName("endElapsedS")]
    public int EndElapsedS { get; set; }

    [JsonPropertyName("startDistanceM")]
    public double? StartDistanceM { get; set; }

    /// <summary>
    /// FIT lap intensity, snake_case: active, rest, warmup, cooldown, recovery, interval, other
    /// (unknown future values pass through snake_cased). Null for distance splits and for sources
    /// without it (HealthKit, Strava CSV, GPX); keep a pace-based fallback.
    /// </summary>
    [JsonPropertyName("intensity")]
    public string? Intensity { get; set; }

    /// <summary>
    /// FIT lap_trigger, snake_case: manual, time, distance, position_start, position_lap,
    /// position_waypoint, position_marked, session_end, fitness_equipment. Null when absent.
    /// </summary>
    [JsonPropertyName("trigger")]
    public string? Trigger { get; set; }

    /// <summary>
    /// FIT wkt_step_index: structured-workout step this lap belongs to. Null when absent.
    /// </summary>
    [JsonPropertyName("stepIndex")]
    public int? StepIndex { get; set; }
}
