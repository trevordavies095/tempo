using System.ComponentModel.DataAnnotations;

namespace Tempo.Api.Models;

/// <summary>
/// Primary AI Coach goal (race or PR). Peelable with the coach module.
/// Instance-level 0-or-1 row in v1. No FK into core Tempo tables.
/// </summary>
public class CoachGoal
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>race | pr</summary>
    [Required]
    [MaxLength(20)]
    public string Type { get; set; } = string.Empty;

    /// <summary>Catalog label from BestEffortService.StandardDistances (e.g. 5K).</summary>
    [Required]
    [MaxLength(50)]
    public string Distance { get; set; } = string.Empty;

    public double DistanceM { get; set; }

    /// <summary>Required when Type is race.</summary>
    public DateOnly? RaceDate { get; set; }

    /// <summary>Required when Type is pr; optional time goal for race.</summary>
    public int? TargetTimeS { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
