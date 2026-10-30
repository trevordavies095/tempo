using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Tempo.Api.Models.Responses;

/// <summary>
/// Body of <c>GET</c> and <c>PUT /settings/thresholds</c>.
/// </summary>
public class ThresholdsDocument
{
    /// <summary>
    /// Athlete-set threshold pace in seconds per kilometre, or null when not set.
    /// </summary>
    [JsonPropertyName("thresholdPaceS")]
    public int? ThresholdPaceS { get; set; }

    /// <summary>
    /// Athlete-set threshold heart rate in BPM, or null when not set.
    /// </summary>
    [JsonPropertyName("thresholdHeartRateBpm")]
    public int? ThresholdHeartRateBpm { get; set; }

    /// <summary>
    /// Read-only suggested threshold pace in seconds per km: Daniels VDOT from the best 5K or 10K effort in the
    /// last 90 days, with T-pace at 88% of VDOT. Present whether or not a threshold is set; null when no effort qualifies.
    /// </summary>
    [JsonPropertyName("suggestedThresholdPaceS")]
    public int? SuggestedThresholdPaceS { get; set; }

    /// <summary>
    /// The best effort the suggestion came from, or null with no suggestion.
    /// </summary>
    [JsonPropertyName("suggestedFrom")]
    public ThresholdSuggestionSourceDocument? SuggestedFrom { get; set; }
}

public class ThresholdSuggestionSourceDocument
{
    /// <summary>
    /// Best-effort distance label: "5K" or "10K".
    /// </summary>
    [Required]
    [JsonPropertyName("distance")]
    public string Distance { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("timeS")]
    public int TimeS { get; set; }

    /// <summary>
    /// UTC date the workout started (yyyy-MM-dd).
    /// </summary>
    [Required]
    [JsonPropertyName("workoutDate")]
    public string WorkoutDate { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("workoutId")]
    public string WorkoutId { get; set; } = string.Empty;
}
