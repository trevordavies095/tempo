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

    /// <summary>
    /// With <c>since</c>: the best time before the PR, or null when it was the first effort at this distance.
    /// Always null without <c>since</c>.
    /// </summary>
    [JsonPropertyName("previousTimeS")]
    public int? PreviousTimeS { get; set; }

    /// <summary>
    /// The workout the previous best was set in; null whenever <c>previousTimeS</c> is.
    /// </summary>
    [JsonPropertyName("previousWorkoutId")]
    public string? PreviousWorkoutId { get; set; }

    /// <summary>
    /// UTC start date (yyyy-MM-dd) of the workout the previous best was set in, the same form as <c>workoutDate</c>;
    /// null whenever <c>previousTimeS</c> is.
    /// </summary>
    [JsonPropertyName("previousWorkoutDate")]
    public string? PreviousWorkoutDate { get; set; }
}
