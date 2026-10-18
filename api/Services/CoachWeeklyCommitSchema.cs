using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tempo.Api.Services;

/// <summary>
/// Fail-closed structural validation for CoachWeeklyCommit.
/// </summary>
public static partial class CoachWeeklyCommitSchema
{
    public const int MaxCoachesNotesLength = 800;

    private static readonly HashSet<string> DayTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "rest", "easy", "quality", "long", "race", "other"
    };

    private static readonly HashSet<string> RunTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "easy", "quality", "long", "race", "other"
    };

    /// <summary>Model-emittable episode categories (recap is human-only).</summary>
    public static readonly HashSet<string> EpisodeCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "race", "breakthrough", "issue", "equipment", "milestone", "protocol_change"
    };

    public static readonly HashSet<string> RuleOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "promote", "revise", "retire"
    };

    public static readonly HashSet<string> RuleTargetLayers = new(StringComparer.OrdinalIgnoreCase)
    {
        "data_interpretation_notes", "key_learnings", "coaching_preferences"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static string? TryParseAndValidate(
        string? rawJson,
        DateOnly weekStartMonday,
        out CoachWeeklyCommit? commit)
    {
        commit = null;
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return "Weekly commit JSON is empty";
        }

        var trimmed = StripMarkdownFences(rawJson.Trim());
        CoachWeeklyCommit? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<CoachWeeklyCommit>(trimmed, JsonOptions);
        }
        catch (JsonException ex)
        {
            return $"Weekly commit JSON parse failed: {ex.Message}";
        }

        if (parsed == null)
        {
            return "Weekly commit JSON deserialized to null";
        }

        var error = Validate(parsed, weekStartMonday);
        if (error != null)
        {
            return error;
        }

        commit = parsed;
        return null;
    }

    public static string Serialize(CoachWeeklyCommit commit) =>
        JsonSerializer.Serialize(commit, JsonOptions);

    public static string? Validate(CoachWeeklyCommit commit, DateOnly weekStartMonday)
    {
        var notes = commit.CoachesNotes?.Trim() ?? string.Empty;
        if (notes.Length == 0)
        {
            return "coachesNotes is required and must be non-empty";
        }

        if (notes.Length > MaxCoachesNotesLength)
        {
            return $"coachesNotes must be at most {MaxCoachesNotesLength} characters";
        }

        commit.CoachesNotes = notes;

        if (commit.Days == null || commit.Days.Count != 7)
        {
            return "days must contain exactly 7 entries (Mon–Sun)";
        }

        for (var i = 0; i < 7; i++)
        {
            var expected = weekStartMonday.AddDays(i);
            var day = commit.Days[i];
            if (!DateOnly.TryParse(day.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return $"days[{i}].date must be yyyy-MM-dd";
            }

            if (date != expected)
            {
                return $"days[{i}].date must be {expected:yyyy-MM-dd} (got {date:yyyy-MM-dd})";
            }

            var dayType = (day.DayType ?? string.Empty).Trim().ToLowerInvariant();
            if (!DayTypes.Contains(dayType))
            {
                return $"days[{i}].dayType must be rest|easy|quality|long|race|other";
            }

            day.DayType = dayType;

            if (dayType == "rest")
            {
                if (day.Run != null && !IsRestRun(day.Run))
                {
                    return $"days[{i}] is rest but has a non-rest run";
                }
            }
            else
            {
                if (day.Run == null)
                {
                    return $"days[{i}] ({dayType}) requires a run object";
                }

                var runType = (day.Run.Type ?? string.Empty).Trim().ToLowerInvariant();
                if (!RunTypes.Contains(runType))
                {
                    return $"days[{i}].run.type must be easy|quality|long|race|other";
                }

                day.Run.Type = runType;
                if (day.Run.DistanceM < 0)
                {
                    return $"days[{i}].run.distanceM must be >= 0";
                }

                CoachDistanceNormalizer.NormalizeCommitRun(day.Run);
            }
        }

        commit.SafetyFlags ??= [];
        commit.Layer1Patches ??= [];
        commit.Layer2EpisodeDrafts ??= [];
        commit.Layer3RuleProposals ??= [];
        commit.Callouts ??= [];
        if (commit.Callouts.Count > 5)
        {
            return "callouts must have at most 5 items";
        }

        for (var i = 0; i < commit.Layer1Patches.Count; i++)
        {
            var patch = commit.Layer1Patches[i];
            if (string.IsNullOrWhiteSpace(patch.FieldPath))
            {
                return $"layer1Patches[{i}].fieldPath is required";
            }

            patch.FieldPath = patch.FieldPath.Trim();
            patch.Rationale ??= string.Empty;
        }

        for (var i = 0; i < commit.Layer2EpisodeDrafts.Count; i++)
        {
            var draft = commit.Layer2EpisodeDrafts[i];
            if (!DateOnly.TryParse(draft.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                return $"layer2EpisodeDrafts[{i}].date must be yyyy-MM-dd";
            }

            var category = (draft.Category ?? string.Empty).Trim().ToLowerInvariant();
            if (string.Equals(category, "recap", StringComparison.OrdinalIgnoreCase))
            {
                return $"layer2EpisodeDrafts[{i}].category 'recap' is human-only and cannot be emitted by the model";
            }

            if (!EpisodeCategories.Contains(category))
            {
                return $"layer2EpisodeDrafts[{i}].category must be race|breakthrough|issue|equipment|milestone|protocol_change";
            }

            draft.Category = category;

            if (string.IsNullOrWhiteSpace(draft.Summary))
            {
                return $"layer2EpisodeDrafts[{i}].summary is required";
            }

            draft.Summary = draft.Summary.Trim();
            if (draft.SourceWeekEnding != null
                && !DateOnly.TryParse(draft.SourceWeekEnding, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                return $"layer2EpisodeDrafts[{i}].sourceWeekEnding must be yyyy-MM-dd when present";
            }
        }

        for (var i = 0; i < commit.Layer3RuleProposals.Count; i++)
        {
            var proposal = commit.Layer3RuleProposals[i];
            var operation = (proposal.Operation ?? string.Empty).Trim().ToLowerInvariant();
            if (!RuleOperations.Contains(operation))
            {
                return $"layer3RuleProposals[{i}].operation must be promote|revise|retire";
            }

            proposal.Operation = operation;

            var targetLayer = (proposal.TargetLayer ?? string.Empty).Trim().ToLowerInvariant();
            if (!RuleTargetLayers.Contains(targetLayer))
            {
                return $"layer3RuleProposals[{i}].targetLayer must be data_interpretation_notes|key_learnings|coaching_preferences";
            }

            proposal.TargetLayer = targetLayer;
            proposal.Evidence ??= [];
        }

        return null;
    }

    public static bool IsRestDay(CoachWeeklyCommitDay day)
    {
        if (string.Equals(day.DayType, "rest", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return day.Run == null || IsRestRun(day.Run);
    }

    private static bool IsRestRun(CoachWeeklyCommitRun run)
        => string.Equals(run.Type, "rest", StringComparison.OrdinalIgnoreCase)
           || (run.DistanceM <= 0 && string.IsNullOrWhiteSpace(run.Type));

    private static string StripMarkdownFences(string text)
    {
        var match = FenceRegex().Match(text);
        return match.Success ? match.Groups[1].Value.Trim() : text;
    }

    [GeneratedRegex(@"^```(?:json)?\s*(.*?)\s*```$", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex FenceRegex();
}
