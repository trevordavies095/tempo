using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Tempo.Api.Models.Responses;

/// <summary>
/// One match from <c>GET /workouts/{id}/similar-routes</c> (root JSON array).
/// </summary>
public class SimilarRouteDocument
{
    [Required]
    [JsonPropertyName("workoutId")]
    public Guid WorkoutId { get; set; }

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

    [JsonPropertyName("similarityScore")]
    public double? SimilarityScore { get; set; }

    /// <summary>Negative = faster than current workout.</summary>
    [JsonPropertyName("timeDifferenceS")]
    public int? TimeDifferenceS { get; set; }

    /// <summary>Negative = faster pace than current workout.</summary>
    [JsonPropertyName("paceDifferenceS")]
    public double? PaceDifferenceS { get; set; }

    [JsonPropertyName("relativeEffort")]
    public int? RelativeEffort { get; set; }

    [JsonPropertyName("elevGainM")]
    public double? ElevGainM { get; set; }
}
