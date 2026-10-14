using System.ComponentModel.DataAnnotations;

namespace Tempo.Api.Models;

public static class CoachJobKinds
{
    public const string Generate = "generate";
    public const string Adjust = "adjust";
}

public static class CoachJobStatuses
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

public static class CoachJobErrorCodes
{
    public const string RateLimited = "rate_limited";
    public const string Auth = "auth";
    public const string Billing = "billing";
    public const string Timeout = "timeout";
    public const string Unknown = "unknown";
    public const string Validation = "validation";
    public const string Interrupted = "interrupted";
}

/// <summary>
/// Peelable background coach generate/adjust job (import-job shaped; not mutexed with imports).
/// </summary>
public class CoachJob
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(32)]
    public string Status { get; set; } = CoachJobStatuses.Queued;

    [Required]
    [MaxLength(32)]
    public string Kind { get; set; } = CoachJobKinds.Generate;

    public Guid? AdjustmentId { get; set; }

    public DateOnly WeekStartDate { get; set; }

    public Guid PreflightId { get; set; }

    [MaxLength(32)]
    public string? ErrorCode { get; set; }

    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    public int? InputTokens { get; set; }

    public int? OutputTokens { get; set; }

    public bool SchemaRepairAttempted { get; set; }

    public Guid? ResultWeekId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
