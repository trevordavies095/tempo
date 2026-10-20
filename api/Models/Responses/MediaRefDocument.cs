using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Tempo.Api.Models.Responses;

/// <summary>
/// Media stub on workout list items.
/// </summary>
public class MediaRefDocument
{
    [Required]
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [Required]
    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = string.Empty;
}
