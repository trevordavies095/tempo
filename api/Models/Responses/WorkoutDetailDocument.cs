using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Tempo.Api.Models.Responses;

/// <summary>
/// Body of <c>GET /workouts/{id}</c>.
/// </summary>
public class WorkoutDetailDocument
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

    /// <summary>
    /// Max cadence in steps per minute (both feet). Despite the <c>*Rpm</c> JSON name, values are total steps/min (~160 typical), not single-leg RPM (~80).
    /// </summary>
    [JsonPropertyName("maxCadenceRpm")]
    public int? MaxCadenceRpm { get; set; }

    /// <summary>
    /// Average cadence in steps per minute (both feet). Despite the <c>*Rpm</c> JSON name, values are total steps/min (~160 typical), not single-leg RPM (~80).
    /// </summary>
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

    [JsonPropertyName("heartRateZoneTimes")]
    public List<HrZoneTimeDocument>? HeartRateZoneTimes { get; set; }

    [JsonPropertyName("rpe")]
    public int? Rpe { get; set; }

    [JsonPropertyName("runType")]
    public string? RunType { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("device")]
    public string? Device { get; set; }

    [JsonPropertyName("healthKitUuid")]
    public Guid? HealthKitUuid { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("shoeId")]
    public Guid? ShoeId { get; set; }

    [JsonPropertyName("shoe")]
    public WorkoutShoeRefDocument? Shoe { get; set; }

    /// <summary>Normalized weather object, or null.</summary>
    [JsonPropertyName("weather")]
    public object? Weather { get; set; }

    [JsonPropertyName("rawGpxData")]
    public object? RawGpxData { get; set; }

    [JsonPropertyName("rawFitData")]
    public object? RawFitData { get; set; }

    [JsonPropertyName("rawStravaData")]
    public object? RawStravaData { get; set; }

    [JsonPropertyName("rawHealthKitData")]
    public object? RawHealthKitData { get; set; }

    [Required]
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("route")]
    public object? Route { get; set; }

    [Required]
    [JsonPropertyName("splits")]
    public List<WorkoutSplitDocument> Splits { get; set; } = [];
}
