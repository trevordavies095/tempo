using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tempo.Api.Services;

/// <summary>
/// Fail-closed structural validation for CoachExchangeReply.
/// Rejects memory channels; one LM schema-repair attempt is allowed by the worker.
/// </summary>
public static partial class CoachExchangeReplySchema
{
    public const int MaxSectionLength = 1200;
    public const int MaxSafetyFlags = 5;
    public const int MaxSafetyFlagLength = 400;
    public const int MaxSuggestedSummaryLength = 400;
    public const int MaxSuggestedNoteLength = 2000;
    public const int MaxAffectedDates = 7;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static string? TryParseAndValidate(string? rawJson, out CoachExchangeReply? reply)
    {
        reply = null;
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return "Exchange reply JSON is empty";
        }

        var trimmed = StripMarkdownFences(rawJson.Trim());

        // Reject memory channels before deserialize so unknown weekly keys fail closed.
        if (ContainsMemoryChannel(trimmed))
        {
            return "Exchange reply must not include memory channels (layer1Patches / layer2EpisodeDrafts / layer3RuleProposals)";
        }

        CoachExchangeReply? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<CoachExchangeReply>(trimmed, JsonOptions);
        }
        catch (JsonException ex)
        {
            return $"Exchange reply JSON parse failed: {ex.Message}";
        }

        if (parsed == null)
        {
            return "Exchange reply JSON deserialized to null";
        }

        var error = Validate(parsed);
        if (error != null)
        {
            return error;
        }

        reply = parsed;
        return null;
    }

    public static string Serialize(CoachExchangeReply reply) =>
        JsonSerializer.Serialize(reply, JsonOptions);

    public static string? SerializeSuggestedChange(CoachExchangeSuggestedChange? change) =>
        change == null ? null : JsonSerializer.Serialize(change, JsonOptions);

    public static string? Validate(CoachExchangeReply reply)
    {
        var what = reply.WhatHappened?.Trim() ?? string.Empty;
        if (what.Length == 0)
        {
            return "whatHappened is required and must be non-empty";
        }

        if (what.Length > MaxSectionLength)
        {
            return $"whatHappened must be at most {MaxSectionLength} characters";
        }

        reply.WhatHappened = what;

        var why = reply.WhyItLooksLikeThat?.Trim() ?? string.Empty;
        if (why.Length == 0)
        {
            return "whyItLooksLikeThat is required and must be non-empty";
        }

        if (why.Length > MaxSectionLength)
        {
            return $"whyItLooksLikeThat must be at most {MaxSectionLength} characters";
        }

        reply.WhyItLooksLikeThat = why;

        var next = reply.WhatItMeansNext?.Trim() ?? string.Empty;
        if (next.Length == 0)
        {
            return "whatItMeansNext is required and must be non-empty";
        }

        if (next.Length > MaxSectionLength)
        {
            return $"whatItMeansNext must be at most {MaxSectionLength} characters";
        }

        reply.WhatItMeansNext = next;

        var verdict = reply.Verdict?.Trim() ?? string.Empty;
        if (!CoachExchangeVerdicts.All.Contains(verdict))
        {
            return "verdict must be one of on_track|noise|watch|concern";
        }

        reply.Verdict = NormalizeVerdict(verdict);

        reply.SafetyFlags ??= [];
        if (reply.SafetyFlags.Count > MaxSafetyFlags)
        {
            return $"safetyFlags must have at most {MaxSafetyFlags} entries";
        }

        for (var i = 0; i < reply.SafetyFlags.Count; i++)
        {
            var flag = reply.SafetyFlags[i]?.Trim() ?? string.Empty;
            if (flag.Length == 0)
            {
                return $"safetyFlags[{i}] must be non-empty";
            }

            if (flag.Length > MaxSafetyFlagLength)
            {
                return $"safetyFlags[{i}] must be at most {MaxSafetyFlagLength} characters";
            }

            reply.SafetyFlags[i] = flag;
        }

        if (reply.SuggestedChange != null)
        {
            var sc = reply.SuggestedChange;
            var summary = sc.Summary?.Trim() ?? string.Empty;
            if (summary.Length == 0)
            {
                return "suggestedChange.summary is required when suggestedChange is present";
            }

            if (summary.Length > MaxSuggestedSummaryLength)
            {
                return $"suggestedChange.summary must be at most {MaxSuggestedSummaryLength} characters";
            }

            sc.Summary = summary;

            var note = sc.Note?.Trim() ?? string.Empty;
            if (note.Length == 0)
            {
                return "suggestedChange.note is required when suggestedChange is present";
            }

            if (note.Length > MaxSuggestedNoteLength)
            {
                return $"suggestedChange.note must be at most {MaxSuggestedNoteLength} characters";
            }

            sc.Note = note;

            sc.AffectedDates ??= [];
            if (sc.AffectedDates.Count == 0)
            {
                return "suggestedChange.affectedDates must be non-empty when suggestedChange is present";
            }

            if (sc.AffectedDates.Count > MaxAffectedDates)
            {
                return $"suggestedChange.affectedDates must have at most {MaxAffectedDates} entries";
            }

            for (var i = 0; i < sc.AffectedDates.Count; i++)
            {
                var d = sc.AffectedDates[i]?.Trim() ?? string.Empty;
                if (!DateOnly.TryParse(d, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    return $"suggestedChange.affectedDates[{i}] must be yyyy-MM-dd";
                }

                sc.AffectedDates[i] = d;
            }
        }

        return null;
    }

    /// <summary>
    /// Clears suggestedChange when there is no committed plan or no remaining days after today local.
    /// </summary>
    public static void StripSuggestedChangeIfNeeded(
        CoachExchangeReply reply,
        bool hasCommittedPlan,
        IReadOnlyCollection<string>? remainingDates)
    {
        if (reply.SuggestedChange == null)
        {
            return;
        }

        if (!hasCommittedPlan || remainingDates == null || remainingDates.Count == 0)
        {
            reply.SuggestedChange = null;
            return;
        }

        var allowed = remainingDates.ToHashSet(StringComparer.Ordinal);
        var filtered = reply.SuggestedChange.AffectedDates
            .Where(d => allowed.Contains(d))
            .ToList();
        if (filtered.Count == 0)
        {
            reply.SuggestedChange = null;
            return;
        }

        reply.SuggestedChange.AffectedDates = filtered;
    }

    private static bool ContainsMemoryChannel(string json)
    {
        return MemoryChannelRegex().IsMatch(json);
    }

    private static string NormalizeVerdict(string verdict) =>
        verdict.ToLowerInvariant() switch
        {
            "on_track" => CoachExchangeVerdicts.OnTrack,
            "noise" => CoachExchangeVerdicts.Noise,
            "watch" => CoachExchangeVerdicts.Watch,
            "concern" => CoachExchangeVerdicts.Concern,
            _ => verdict
        };

    private static string StripMarkdownFences(string raw)
    {
        if (!raw.StartsWith("```", StringComparison.Ordinal))
        {
            return raw;
        }

        var lines = raw.Split('\n');
        if (lines.Length < 3)
        {
            return raw;
        }

        var end = lines.Length - 1;
        while (end > 0 && string.IsNullOrWhiteSpace(lines[end]))
        {
            end--;
        }

        if (!lines[end].TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            return raw;
        }

        return string.Join('\n', lines[1..end]);
    }

    [GeneratedRegex(
        @"""(layer1Patches|layer2EpisodeDrafts|layer3RuleProposals)""\s*:",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MemoryChannelRegex();
}
