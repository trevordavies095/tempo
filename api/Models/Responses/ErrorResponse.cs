using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Tempo.Api.Models.Responses;

/// <summary>
/// Standard error body used by many Tempo endpoints: <c>{ "error": "…" }</c>.
/// </summary>
public class ErrorResponse
{
    [Required]
    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;
}
