using System.Text.Json;
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

    public List<CoachLayer1Patch> Layer1Patches { get; set; } = [];

    public List<CoachLayer2EpisodeDraft> Layer2EpisodeDrafts { get; set; } = [];

    public List<CoachLayer3RuleProposal> Layer3RuleProposals { get; set; } = [];

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

public sealed class CoachProposalValidity
{
    public bool Ok { get; set; } = true;

    public string? Reason { get; set; }
}

public sealed class CoachLayer1Patch
{
    public string FieldPath { get; set; } = string.Empty;

    public JsonElement? OldValue { get; set; }

    public JsonElement? NewValue { get; set; }

    public string Rationale { get; set; } = string.Empty;

    public CoachProposalValidity? Validity { get; set; }
}

public sealed class CoachLayer2EpisodeDraft
{
    /// <summary>ISO date yyyy-MM-dd when the event happened.</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>race | breakthrough | issue | equipment | milestone | protocol_change (not recap from model)</summary>
    public string Category { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string? SourceWeekEnding { get; set; }

    public CoachProposalValidity? Validity { get; set; }
}

public sealed class CoachLayer3RuleProposal
{
    /// <summary>promote | revise | retire</summary>
    public string Operation { get; set; } = string.Empty;

    public string? RuleText { get; set; }

    public List<string> Evidence { get; set; } = [];

    public string? TargetRuleId { get; set; }

    /// <summary>data_interpretation_notes | key_learnings | coaching_preferences</summary>
    public string TargetLayer { get; set; } = string.Empty;

    /// <summary>
    /// Snapshot of the active target rule's text at stamp time (revise/retire only).
    /// Lets review UI show a diff without a Memory list API.
    /// </summary>
    public string? CurrentRuleText { get; set; }

    public CoachProposalValidity? Validity { get; set; }
}
