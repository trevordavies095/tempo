using System.Globalization;
using System.Text.Json;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Stamps validity on typed memory proposals. Invalid items do not fail the week.
/// </summary>
public static class CoachMemoryProposalValidator
{
    public const int MaxEpisodeSummaryLength = 400;

    public static readonly HashSet<string> Layer1Allowlist = new(StringComparer.Ordinal)
    {
        "paces.easySecPerKm",
        "paces.tempoSecPerKm",
        "paces.thresholdSecPerKm",
        "paces.intervalSecPerKm",
        "hrZones.maxHeartRateBpm",
        "hrZones.restingHeartRateBpm",
        "raceStatus.targetTimeS",
        "raceStatus.notes",
        "trainingPhase"
    };

    private static readonly HashSet<string> TrainingPhases = new(StringComparer.OrdinalIgnoreCase)
    {
        CoachTrainingPhases.Base,
        CoachTrainingPhases.Build,
        CoachTrainingPhases.Peak,
        CoachTrainingPhases.Taper,
        CoachTrainingPhases.Recovery
    };

    public static void Stamp(
        CoachWeeklyCommit commit,
        DateOnly weekStartMonday,
        IReadOnlySet<string> existingEpisodeIds,
        IReadOnlyDictionary<string, CoachRule> activeRulesById,
        DateOnly? asOfDate = null)
    {
        commit.Layer1Patches ??= [];
        commit.Layer2EpisodeDrafts ??= [];
        commit.Layer3RuleProposals ??= [];

        var today = asOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var weekEnding = weekStartMonday.AddDays(6);
        var earliest = today.AddDays(-365);

        foreach (var patch in commit.Layer1Patches)
        {
            patch.Validity = ValidateLayer1Patch(patch);
        }

        // Track prospective ids within this commit so duplicate drafts collide.
        var claimedEpisodeIds = new HashSet<string>(existingEpisodeIds, StringComparer.Ordinal);

        foreach (var draft in commit.Layer2EpisodeDrafts)
        {
            draft.Validity = ValidateEpisodeDraft(draft, weekStartMonday, weekEnding, earliest, claimedEpisodeIds);
            if (draft.Validity.Ok
                && DateOnly.TryParse(draft.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                claimedEpisodeIds.Add(CoachMemoryIds.ProspectiveEpisodeId(date, draft.Category, draft.Summary));
            }
        }

        foreach (var proposal in commit.Layer3RuleProposals)
        {
            proposal.Validity = ValidateRuleProposal(proposal, activeRulesById);
        }
    }

    public static CoachProposalValidity ValidateLayer1Patch(CoachLayer1Patch patch)
    {
        var path = (patch.FieldPath ?? string.Empty).Trim();
        if (!Layer1Allowlist.Contains(path))
        {
            return Invalid($"fieldPath '{path}' is not in the L1 allowlist");
        }

        if (patch.NewValue is null || patch.NewValue.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            if (path == "trainingPhase" || path == "raceStatus.notes")
            {
                return Ok();
            }

            return Invalid($"newValue is required for '{path}'");
        }

        var value = patch.NewValue.Value;
        return path switch
        {
            "paces.easySecPerKm" or "paces.tempoSecPerKm" or "paces.thresholdSecPerKm" or "paces.intervalSecPerKm"
                => RequirePositiveInt(value, path),
            "hrZones.maxHeartRateBpm" or "hrZones.restingHeartRateBpm"
                => RequirePositiveInt(value, path),
            "raceStatus.targetTimeS"
                => RequirePositiveInt(value, path),
            "raceStatus.notes"
                => value.ValueKind == JsonValueKind.String
                    ? Ok()
                    : Invalid("raceStatus.notes newValue must be a string or null"),
            "trainingPhase"
                => ValidateTrainingPhase(value),
            _ => Invalid($"fieldPath '{path}' is not in the L1 allowlist")
        };
    }

    private static CoachProposalValidity ValidateEpisodeDraft(
        CoachLayer2EpisodeDraft draft,
        DateOnly weekStartMonday,
        DateOnly weekEnding,
        DateOnly earliest,
        IReadOnlySet<string> claimedEpisodeIds)
    {
        if (!DateOnly.TryParse(draft.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return Invalid("date must be yyyy-MM-dd");
        }

        var inThisWeek = date >= weekStartMonday && date <= weekEnding;
        var inWindow = date >= earliest && date <= weekEnding;
        if (!inThisWeek && !inWindow)
        {
            return Invalid($"date {date:yyyy-MM-dd} is outside the last 365 days and this week");
        }

        var category = (draft.Category ?? string.Empty).Trim().ToLowerInvariant();
        if (string.Equals(category, CoachEpisodeCategories.Recap, StringComparison.OrdinalIgnoreCase))
        {
            return Invalid("category 'recap' is human-only");
        }

        if (!CoachWeeklyCommitSchema.EpisodeCategories.Contains(category))
        {
            return Invalid($"category '{category}' is not allowed");
        }

        var summary = (draft.Summary ?? string.Empty).Trim();
        if (summary.Length == 0)
        {
            return Invalid("summary is required");
        }

        if (summary.Length > MaxEpisodeSummaryLength)
        {
            return Invalid($"summary must be at most {MaxEpisodeSummaryLength} characters");
        }

        var prospectiveId = CoachMemoryIds.ProspectiveEpisodeId(date, category, summary);
        if (claimedEpisodeIds.Contains(prospectiveId))
        {
            return Invalid("already in memory");
        }

        return Ok();
    }

    private static CoachProposalValidity ValidateRuleProposal(
        CoachLayer3RuleProposal proposal,
        IReadOnlyDictionary<string, CoachRule> activeRulesById)
    {
        var operation = (proposal.Operation ?? string.Empty).Trim().ToLowerInvariant();
        if (!CoachWeeklyCommitSchema.RuleOperations.Contains(operation))
        {
            return Invalid($"operation '{operation}' is not allowed");
        }

        var targetLayer = (proposal.TargetLayer ?? string.Empty).Trim().ToLowerInvariant();
        if (!CoachWeeklyCommitSchema.RuleTargetLayers.Contains(targetLayer))
        {
            return Invalid($"targetLayer '{targetLayer}' is not allowed");
        }

        proposal.Evidence ??= [];

        if (operation == "promote")
        {
            if (!string.IsNullOrWhiteSpace(proposal.TargetRuleId))
            {
                return Invalid("promote requires targetRuleId to be null");
            }

            if (string.IsNullOrWhiteSpace(proposal.RuleText))
            {
                return Invalid("promote requires non-empty ruleText");
            }

            if (proposal.Evidence.Count == 0 || proposal.Evidence.All(string.IsNullOrWhiteSpace))
            {
                return Invalid("promote requires non-empty evidence");
            }

            return Ok();
        }

        if (string.IsNullOrWhiteSpace(proposal.TargetRuleId))
        {
            return Invalid($"{operation} requires targetRuleId of an existing active rule");
        }

        if (!activeRulesById.ContainsKey(proposal.TargetRuleId.Trim()))
        {
            return Invalid($"targetRuleId '{proposal.TargetRuleId}' does not resolve to an active rule");
        }

        if (operation == "revise" && string.IsNullOrWhiteSpace(proposal.RuleText))
        {
            return Invalid("revise requires non-empty ruleText");
        }

        return Ok();
    }

    private static CoachProposalValidity ValidateTrainingPhase(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return Ok();
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return Invalid("trainingPhase newValue must be a string or null");
        }

        var phase = value.GetString()?.Trim();
        if (string.IsNullOrEmpty(phase))
        {
            return Ok();
        }

        return TrainingPhases.Contains(phase)
            ? Ok()
            : Invalid("trainingPhase must be base|build|peak|taper|recovery|null");
    }

    private static CoachProposalValidity RequirePositiveInt(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) && n > 0)
        {
            return Ok();
        }

        return Invalid($"{path} newValue must be a positive integer");
    }

    private static CoachProposalValidity Ok() => new() { Ok = true, Reason = null };

    private static CoachProposalValidity Invalid(string reason) => new() { Ok = false, Reason = reason };
}
