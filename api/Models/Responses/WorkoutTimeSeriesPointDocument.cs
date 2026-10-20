using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Tempo.Api.Models.Responses;

/// <summary>
/// One sample from <c>GET /workouts/{id}/time-series</c>.
/// </summary>
public class WorkoutTimeSeriesPointDocument
{
    [Required]
    [JsonPropertyName("elapsedSeconds")]
    public int ElapsedSeconds { get; set; }

    [JsonPropertyName("distanceM")]
    public double? DistanceM { get; set; }

    [JsonPropertyName("heartRateBpm")]
    public int? HeartRateBpm { get; set; }

    [JsonPropertyName("cadenceRpm")]
    public int? CadenceRpm { get; set; }

    [JsonPropertyName("powerWatts")]
    public int? PowerWatts { get; set; }

    [JsonPropertyName("speedMps")]
    public double? SpeedMps { get; set; }

    [JsonPropertyName("gradePercent")]
    public double? GradePercent { get; set; }

    [JsonPropertyName("elevationM")]
    public double? ElevationM { get; set; }

    [JsonPropertyName("temperatureC")]
    public int? TemperatureC { get; set; }

    [JsonPropertyName("verticalSpeedMps")]
    public double? VerticalSpeedMps { get; set; }
}
