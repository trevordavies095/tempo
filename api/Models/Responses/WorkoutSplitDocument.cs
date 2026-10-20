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
}
