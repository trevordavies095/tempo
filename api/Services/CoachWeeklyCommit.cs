using System.Text.Json.Serialization;

namespace Tempo.Api.Services;

/// <summary>
/// Tempo-owned weekly commit contract (camelCase JSON) produced by the protocol brain.
/// </summary>
public sealed class CoachWeeklyCommit
{
    public string CoachesNotes { get; set; } = string.Empty;

    public List<CoachWeeklyCommitDay> Days { get; set; } = [];

    public List<CoachWeeklyCommitSafetyFlag> SafetyFlags { get; set; } = [];

    public List<CoachWeeklyCommitMemoryProposal> MemoryProposals { get; set; } = [];

    public string? ClarifyingQuestion { get; set; }

    public List<string> Callouts { get; set; } = [];
}

public sealed class CoachWeeklyCommitDay
{
    /// <summary>Local calendar date yyyy-MM-dd.</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>rest | easy | quality | long | race | other</summary>
    public string DayType { get; set; } = string.Empty;

    public CoachWeeklyCommitRun? Run { get; set; }

    /// <summary>Optional strength; null in most v1 plans.</summary>
    public object? Strength { get; set; }
}

public sealed class CoachWeeklyCommitRun
{
    /// <summary>easy | quality | long | race | other</summary>
    public string Type { get; set; } = string.Empty;

    public double DistanceM { get; set; }

    public string? Structure { get; set; }

    public string? PaceOrHr { get; set; }

    public Guid? ShoeId { get; set; }

    public string? Notes { get; set; }
}

public sealed class CoachWeeklyCommitSafetyFlag
{
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string Severity { get; set; } = "info";

    public bool RequiresResponse { get; set; }
}

public sealed class CoachWeeklyCommitMemoryProposal
{
    public string Layer { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;
}
