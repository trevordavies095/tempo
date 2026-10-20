using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Tempo.Api.Models.Responses;

/// <summary>
/// One row from <c>GET /workouts</c>.
/// </summary>
public class WorkoutListItemDocument
{
    [Required]
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [Required]
    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; }

    [Required]
    [JsonPropertyName("durationS")]
    public int DurationS { get; set; }

    [Required]
    [JsonPropertyName("distanceM")]
    public double DistanceM { get; set; }

    [Required]
    [JsonPropertyName("avgPaceS")]
    public double AvgPaceS { get; set; }

    [JsonPropertyName("elevGainM")]
    public double? ElevGainM { get; set; }

    [JsonPropertyName("elevLossM")]
    public double? ElevLossM { get; set; }

    [JsonPropertyName("minElevM")]
    public double? MinElevM { get; set; }

    [JsonPropertyName("maxElevM")]
    public double? MaxElevM { get; set; }

    [JsonPropertyName("maxSpeedMps")]
    public double? MaxSpeedMps { get; set; }

    [JsonPropertyName("avgSpeedMps")]
    public double? AvgSpeedMps { get; set; }

    [JsonPropertyName("movingTimeS")]
    public int? MovingTimeS { get; set; }

    [JsonPropertyName("timerTimeS")]
    public int? TimerTimeS { get; set; }

    [JsonPropertyName("maxHeartRateBpm")]
    public int? MaxHeartRateBpm { get; set; }

    [JsonPropertyName("avgHeartRateBpm")]
    public int? AvgHeartRateBpm { get; set; }

    [JsonPropertyName("minHeartRateBpm")]
    public int? MinHeartRateBpm { get; set; }

    [JsonPropertyName("maxCadenceRpm")]
    public int? MaxCadenceRpm { get; set; }

    [JsonPropertyName("avgCadenceRpm")]
    public int? AvgCadenceRpm { get; set; }

    [JsonPropertyName("maxPowerWatts")]
    public int? MaxPowerWatts { get; set; }

    [JsonPropertyName("avgPowerWatts")]
    public int? AvgPowerWatts { get; set; }

    [JsonPropertyName("calories")]
    public int? Calories { get; set; }

    [JsonPropertyName("relativeEffort")]
    public int? RelativeEffort { get; set; }

    [JsonPropertyName("rpe")]
    public int? Rpe { get; set; }

    [JsonPropertyName("runType")]
    public string? RunType { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("device")]
    public string? Device { get; set; }

    [JsonPropertyName("healthKitUuid")]
    public Guid? HealthKitUuid { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [Required]
    [JsonPropertyName("hasRoute")]
    public bool HasRoute { get; set; }

    /// <summary>GeoJSON preview or full route, or null.</summary>
    [JsonPropertyName("route")]
    public object? Route { get; set; }

    [Required]
    [JsonPropertyName("splitsCount")]
    public int SplitsCount { get; set; }

    [Required]
    [JsonPropertyName("media")]
    public List<MediaRefDocument> Media { get; set; } = [];
}
