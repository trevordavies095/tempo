using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Tempo.Api.Models;

public static class CoachExchangeStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Ready = "ready";
    public const string Failed = "failed";

    public static readonly HashSet<string> Active = new(StringComparer.Ordinal)
    {
        Queued,
        Running
    };
}

public static class CoachPrivacyPreflightKinds
{
    public const string Weekly = "weekly";
    public const string Exchange = "exchange";
}

/// <summary>
/// Peelable mid-week one-shot coach exchange (message or attached run). Never writes CoachWeek.
/// </summary>
public class CoachExchange
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Plan-week Monday for this exchange (workout local date or today local).</summary>
    public DateOnly WeekStartDate { get; set; }

    /// <summary>Optional workout id with no FK into core workouts.</summary>
    public Guid? WorkoutId { get; set; }

    [Required]
    [MaxLength(4000)]
    public string AthleteText { get; set; } = string.Empty;

    public Guid PreflightId { get; set; }

    public Guid? JobId { get; set; }

    [Column(TypeName = "jsonb")]
    public string? ReplyJson { get; set; }

    /// <summary>Denormalized optional suggestedChange from a validated reply.</summary>
    [Column(TypeName = "jsonb")]
    public string? SuggestedChangeJson { get; set; }

    [Required]
    [MaxLength(32)]
    public string Status { get; set; } = CoachExchangeStatuses.Queued;

    public int? InputTokens { get; set; }

    public int? OutputTokens { get; set; }

    [MaxLength(32)]
    public string? ErrorCode { get; set; }

    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    /// <summary>Reserved for Accept N+1 lock (issue 32). Default false; not enforced in issue 29.</summary>
    public bool Locked { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }
}
