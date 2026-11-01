using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Tempo.Api.Models.Responses;

/// <summary>
/// Body of <c>GET /settings/heart-rate-zones</c>.
/// </summary>
public class HeartRateZonesDocument
{
    [Required]
    [JsonPropertyName("calculationMethod")]
    public string CalculationMethod { get; set; } = string.Empty;

    [JsonPropertyName("age")]
    public int? Age { get; set; }

    [JsonPropertyName("restingHeartRateBpm")]
    public int? RestingHeartRateBpm { get; set; }

    /// <summary>
    /// Max HR resolved for the calculation method: 220 - age (AgeBased), the entered max (Karvonen),
    /// or the top of zone 5 (Custom). Always present.
    /// </summary>
    [Required]
    [JsonPropertyName("maxHeartRateBpm")]
    public int MaxHeartRateBpm { get; set; }

    /// <summary>
    /// Highest workout max HR in the last 12 months, ignoring readings above 230 bpm. Informational only;
    /// never feeds into <see cref="MaxHeartRateBpm"/>. Null when no workout in the window has HR data.
    /// </summary>
    [JsonPropertyName("observedMaxHeartRateBpm")]
    public int? ObservedMaxHeartRateBpm { get; set; }

    [Required]
    [JsonPropertyName("zones")]
    public List<HeartRateZoneEntryDocument> Zones { get; set; } = [];
}

public class HeartRateZoneEntryDocument
{
    [Required]
    [JsonPropertyName("zoneNumber")]
    public int ZoneNumber { get; set; }

    [Required]
    [JsonPropertyName("minBpm")]
    public int MinBpm { get; set; }

    [Required]
    [JsonPropertyName("maxBpm")]
    public int MaxBpm { get; set; }
}
