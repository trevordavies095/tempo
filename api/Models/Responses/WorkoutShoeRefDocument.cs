using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Tempo.Api.Models.Responses;

/// <summary>
/// Shoe stub on workout detail.
/// </summary>
public class WorkoutShoeRefDocument
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
}
