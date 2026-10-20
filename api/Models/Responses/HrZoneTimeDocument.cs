using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Tempo.Api.Models.Responses;

/// <summary>
/// Time spent in one HR zone on workout detail.
/// </summary>
public class HrZoneTimeDocument
{
    [Required]
    [JsonPropertyName("zone")]
    public int Zone { get; set; }

    [Required]
    [JsonPropertyName("timeS")]
    public int TimeS { get; set; }
}
