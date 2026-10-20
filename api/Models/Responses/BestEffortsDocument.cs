using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Tempo.Api.Models.Responses;

/// <summary>
/// Body of <c>GET /stats/best-efforts</c>.
/// </summary>
public class BestEffortsDocument
{
    [Required]
    [JsonPropertyName("distances")]
    public List<BestEffortDistanceDocument> Distances { get; set; } = [];
}

/// <summary>
/// One best-effort distance entry (matches <c>BestEffortService.BestEffortResult</c> wire shape).
/// </summary>
public class BestEffortDistanceDocument
{
    [Required]
    [JsonPropertyName("distance")]
    public string Distance { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("distanceM")]
    public double DistanceM { get; set; }

    [Required]
    [JsonPropertyName("timeS")]
    public int TimeS { get; set; }

    [Required]
    [JsonPropertyName("workoutId")]
    public string WorkoutId { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("workoutDate")]
    public string WorkoutDate { get; set; } = string.Empty;
}
