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
}
