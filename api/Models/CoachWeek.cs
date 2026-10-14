using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Tempo.Api.Models;

public static class CoachWeekStatuses
{
    public const string Draft = "draft";
    public const string Committed = "committed";
    public const string Archived = "archived";
}

/// <summary>
/// Peelable coach week plan. Drafts from generate; apply transitions to committed.
/// </summary>
public class CoachWeek
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateOnly WeekStartDate { get; set; }

    [Required]
    [MaxLength(32)]
    public string Status { get; set; } = CoachWeekStatuses.Draft;

    [Required]
    [Column(TypeName = "jsonb")]
    public string CommitJson { get; set; } = "{}";

    public Guid PreflightId { get; set; }

    public Guid JobId { get; set; }

    [Required]
    [MaxLength(64)]
    public string PayloadHash { get; set; } = string.Empty;

    /// <summary>Post-send privacy receipt: copy of redacted packet that was sent.</summary>
    [Required]
    [Column(TypeName = "jsonb")]
    public string RedactedPayloadJson { get; set; } = "{}";

    /// <summary>Apply audit (acks + accepted memory indexes) set when status becomes committed.</summary>
    [Column(TypeName = "jsonb")]
    public string? ApplyRecordJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
