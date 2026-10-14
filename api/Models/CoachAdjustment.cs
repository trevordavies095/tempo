using System.ComponentModel.DataAnnotations;

namespace Tempo.Api.Models;

public static class CoachAdjustmentStatuses
{
    public const string Started = "started";
    public const string Applied = "applied";
    public const string Cancelled = "cancelled";
}

public static class CoachAdjustmentCategories
{
    public const string Niggle = "niggle";
    public const string Illness = "illness";
    public const string Life = "life";
    public const string Other = "other";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Niggle,
        Illness,
        Life,
        Other
    };
}

/// <summary>
/// Athlete-initiated mid-week adjust session. Starts locked; Anthropic only after consent + generate.
/// </summary>
public class CoachAdjustment
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateOnly WeekStartDate { get; set; }

    public Guid SourceCommittedWeekId { get; set; }

    [Required]
    [MaxLength(2000)]
    public string Reason { get; set; } = string.Empty;

    [MaxLength(32)]
    public string? Category { get; set; }

    /// <summary>Last elapsed local day (inclusive) that must stay historical after splice.</summary>
    public DateOnly LockedThroughDate { get; set; }

    [Required]
    [MaxLength(32)]
    public string Status { get; set; } = CoachAdjustmentStatuses.Started;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
