using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Tempo.Api.Models.Responses;

/// <summary>
/// Body of <c>GET /settings/unit-preference</c>.
/// </summary>
public class UnitPreferenceDocument
{
    /// <summary><c>metric</c> or <c>imperial</c>.</summary>
    [Required]
    [JsonPropertyName("unitPreference")]
    public string UnitPreference { get; set; } = "metric";
}
