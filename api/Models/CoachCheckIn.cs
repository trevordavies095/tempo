using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Tempo.Api.Models;

/// <summary>
/// Peelable weekly Coach check-in (one row per local Monday). Stores optional subjective
/// fields and per-workout note overlays only — live Tempo owns run metrics.
/// </summary>
public class CoachCheckIn
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Local Monday of the Mon–Sun check-in week.</summary>
    public DateOnly WeekStartDate { get; set; }

    /// <summary>Offset used when the athlete last saved (audit); -getTimezoneOffset() convention.</summary>
    public int TimezoneOffsetMinutes { get; set; }

    [MaxLength(500)]
    public string? OverallFeel { get; set; }

    [MaxLength(2000)]
    public string? Niggles { get; set; }

    /// <summary>One-week life constraints (free text); distinct from standing CoachConstraints.</summary>
    [MaxLength(2000)]
    public string? LifeConstraints { get; set; }

    /// <summary>
    /// Check-in-only note overlays as JSON array of { workoutId, notes }.
    /// Does not write back to Workout.Notes.
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string RunNotesJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Per-run note overlay DTO persisted inside CoachCheckIn.RunNotesJson.
/// </summary>
public class CoachCheckInRunNote
{
    public Guid WorkoutId { get; set; }

    public string? Notes { get; set; }
}
