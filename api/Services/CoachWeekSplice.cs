using System.Globalization;
using System.Text.Json;

namespace Tempo.Api.Services;

/// <summary>
/// Mid-week adjust helpers: lock elapsed days from a prior committed plan into a new commit.
/// </summary>
public static class CoachWeekSplice
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static CoachWeeklyCommit SpliceLockedDays(
        CoachWeeklyCommit source,
        CoachWeeklyCommit proposed,
        DateOnly lockedThroughDate)
    {
        var byDate = source.Days.ToDictionary(
            d => d.Date,
            d => CloneDay(d),
            StringComparer.Ordinal);

        var result = new CoachWeeklyCommit
        {
            CoachesNotes = proposed.CoachesNotes,
            SafetyFlags = proposed.SafetyFlags?.Select(CloneFlag).ToList() ?? [],
            MemoryProposals = proposed.MemoryProposals?.Select(CloneProposal).ToList() ?? [],
            ClarifyingQuestion = proposed.ClarifyingQuestion,
            Callouts = proposed.Callouts?.ToList() ?? [],
            Days = []
        };

        foreach (var day in proposed.Days.OrderBy(d => d.Date, StringComparer.Ordinal))
        {
            if (!DateOnly.TryParse(day.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                result.Days.Add(CloneDay(day));
                continue;
            }

            if (date <= lockedThroughDate && byDate.TryGetValue(day.Date, out var locked))
            {
                result.Days.Add(locked);
            }
            else
            {
                result.Days.Add(CloneDay(day));
            }
        }

        return result;
    }

    public static string? ValidateLockedDaysUnchanged(
        CoachWeeklyCommit source,
        CoachWeeklyCommit candidate,
        DateOnly lockedThroughDate)
    {
        var sourceByDate = source.Days.ToDictionary(d => d.Date, StringComparer.Ordinal);
        foreach (var day in candidate.Days)
        {
            if (!DateOnly.TryParse(day.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return $"Invalid day date '{day.Date}'";
            }

            if (date > lockedThroughDate)
            {
                continue;
            }

            if (!sourceByDate.TryGetValue(day.Date, out var locked))
            {
                return $"Locked day {day.Date} is missing from the source committed plan";
            }

            if (!DayEquals(locked, day))
            {
                return $"Locked day {day.Date} must remain unchanged from the prior committed plan";
            }
        }

        // Every source locked day must still be present.
        foreach (var locked in source.Days)
        {
            if (!DateOnly.TryParse(locked.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                continue;
            }

            if (date > lockedThroughDate)
            {
                continue;
            }

            if (candidate.Days.All(d => d.Date != locked.Date))
            {
                return $"Locked day {locked.Date} is missing from the adjust commit";
            }
        }

        return null;
    }

    public static bool DayEquals(CoachWeeklyCommitDay a, CoachWeeklyCommitDay b) =>
        string.Equals(JsonSerializer.Serialize(a, JsonOptions), JsonSerializer.Serialize(b, JsonOptions), StringComparison.Ordinal);

    private static CoachWeeklyCommitDay CloneDay(CoachWeeklyCommitDay day) =>
        JsonSerializer.Deserialize<CoachWeeklyCommitDay>(JsonSerializer.Serialize(day, JsonOptions), JsonOptions)!;

    private static CoachWeeklyCommitSafetyFlag CloneFlag(CoachWeeklyCommitSafetyFlag flag) =>
        JsonSerializer.Deserialize<CoachWeeklyCommitSafetyFlag>(JsonSerializer.Serialize(flag, JsonOptions), JsonOptions)!;

    private static CoachWeeklyCommitMemoryProposal CloneProposal(CoachWeeklyCommitMemoryProposal proposal) =>
        JsonSerializer.Deserialize<CoachWeeklyCommitMemoryProposal>(
            JsonSerializer.Serialize(proposal, JsonOptions),
            JsonOptions)!;
}
