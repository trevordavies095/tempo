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
            }
        }

        commit.SafetyFlags ??= [];
        commit.MemoryProposals ??= [];
        commit.Callouts ??= [];
        if (commit.Callouts.Count > 5)
        {
            return "callouts must have at most 5 items";
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
