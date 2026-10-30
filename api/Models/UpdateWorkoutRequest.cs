using System.Text.Json.Serialization;

namespace Tempo.Api.Models;

/// <summary>
/// Body of <c>PATCH /workouts/{id}</c>. Every field is optional: only fields present in the body are updated,
/// and an explicit <c>null</c> clears the field. Documentation shape only; the handler reads the raw JSON so it
/// can tell an absent field from <c>null</c>.
/// </summary>
public class UpdateWorkoutRequest
{
    /// <summary>
    /// Run type, or null to clear.
    /// </summary>
    [JsonPropertyName("runType")]
    public string? RunType { get; set; }

    /// <summary>
    /// Free-text notes, or null to clear.
    /// </summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>
    /// Workout name (200 characters or less), or null to clear.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Shoe to assign (must exist and not be retired), or null to clear.
    /// </summary>
    [JsonPropertyName("shoeId")]
    public Guid? ShoeId { get; set; }

    /// <summary>
    /// Rate of perceived exertion, a whole number from 1 to 10, or null to clear.
    /// </summary>
    [JsonPropertyName("rpe")]
    public int? Rpe { get; set; }
}
