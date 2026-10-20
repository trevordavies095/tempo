using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Tempo.Api.Models.Responses;

/// <summary>
/// One shoe from <c>GET /shoes</c> (root JSON array).
/// </summary>
public class ShoeListItemDocument
{
    [Required]
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [Required]
    [JsonPropertyName("brand")]
    public string Brand { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("initialMileageM")]
    public double? InitialMileageM { get; set; }

    [Required]
    [JsonPropertyName("isRetired")]
    public bool IsRetired { get; set; }

    [Required]
    [JsonPropertyName("totalMileage")]
    public double TotalMileage { get; set; }

    /// <summary><c>miles</c> or <c>km</c>.</summary>
    [Required]
    [JsonPropertyName("unit")]
    public string Unit { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [Required]
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}
