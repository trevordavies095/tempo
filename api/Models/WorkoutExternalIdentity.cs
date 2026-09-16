using System.ComponentModel.DataAnnotations;

namespace Tempo.Api.Models;

/// <summary>
/// A (source, externalId) row linking a Workout to one upstream system.
/// Used for intake idempotency. Not Workout.Source (provenance).
/// </summary>
public class WorkoutExternalIdentity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid WorkoutId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Source { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string ExternalId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Workout? Workout { get; set; }
}
