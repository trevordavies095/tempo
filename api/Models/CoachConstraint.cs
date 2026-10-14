using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Tempo.Api.Models;

/// <summary>
/// Peelable Coach non-negotiable (B-race, blackout, standing schedule).
/// Multi-row; soft-retire via IsRetired. No FK into core Tempo tables.
/// </summary>
public class CoachConstraint
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>secondary_race | blackout | standing_schedule</summary>
    [Required]
    [MaxLength(40)]
    public string Kind { get; set; } = string.Empty;

    /// <summary>Race date, blackout start, or unused for standing.</summary>
    public DateOnly? StartDate { get; set; }

    /// <summary>Blackout end (null = single-day blackout).</summary>
    public DateOnly? EndDate { get; set; }

    [MaxLength(50)]
    public string? Distance { get; set; }

    public double? DistanceM { get; set; }

    /// <summary>tune_up | all_out | fun_run for secondary_race.</summary>
    [MaxLength(20)]
    public string? Intent { get; set; }

    /// <summary>long_day | no_quality_on | no_run_on for standing_schedule.</summary>
    [MaxLength(40)]
    public string? Preset { get; set; }

    /// <summary>ISO weekdays 1=Mon … 7=Sun (jsonb).</summary>
    [Column(TypeName = "jsonb")]
    public List<int>? Weekdays { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public bool IsRetired { get; set; }

    public DateTime? RetiredAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
