using System.Globalization;
using System.Text.RegularExpressions;

namespace Tempo.Api.Services;

/// <summary>
/// Stable id generation for CoachEpisode and CoachRule rows.
/// </summary>
public static partial class CoachMemoryIds
{
    public const int EpisodeSummarySlugLength = 40;
    public const int RuleTextSlugLength = 48;

    public static string EpisodeId(DateOnly date, string category, string summary, IReadOnlySet<string> existingIds)
    {
        var slug = Slugify(summary, EpisodeSummarySlugLength);
        var baseId = $"ep_{date:yyyy-MM-dd}_{category.Trim().ToLowerInvariant()}_{slug}";
        return Deduplicate(baseId, existingIds);
    }

    public static string RuleId(string ruleText, IReadOnlySet<string> existingIds)
    {
        var slug = Slugify(ruleText, RuleTextSlugLength);
        var baseId = $"rule_{slug}";
        return Deduplicate(baseId, existingIds);
    }

    public static string ProspectiveEpisodeId(DateOnly date, string category, string summary)
        => $"ep_{date:yyyy-MM-dd}_{category.Trim().ToLowerInvariant()}_{Slugify(summary, EpisodeSummarySlugLength)}";

    public static string ProspectiveRuleId(string ruleText)
        => $"rule_{Slugify(ruleText, RuleTextSlugLength)}";

    public static string Slugify(string text, int maxLength)
    {
        var lowered = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (lowered.Length == 0)
        {
            return "item";
        }

        var truncated = lowered.Length <= maxLength ? lowered : lowered[..maxLength];
        var slug = NonAlnumRegex().Replace(truncated, "_");
        slug = CollapseUnderscoreRegex().Replace(slug, "_").Trim('_');
        return string.IsNullOrEmpty(slug) ? "item" : slug;
    }

    public static string Deduplicate(string baseId, IReadOnlySet<string> existingIds)
    {
        if (!existingIds.Contains(baseId))
        {
            return baseId;
        }

        for (var n = 2; n < 10_000; n++)
        {
            var candidate = $"{baseId}_{n.ToString(CultureInfo.InvariantCulture)}";
            if (!existingIds.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{baseId}_{Guid.NewGuid():N}";
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlnumRegex();

    [GeneratedRegex(@"_+")]
    private static partial Regex CollapseUnderscoreRegex();
}
