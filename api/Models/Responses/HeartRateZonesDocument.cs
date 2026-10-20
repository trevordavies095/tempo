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

    [JsonPropertyName("maxHeartRateBpm")]
    public int? MaxHeartRateBpm { get; set; }

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
