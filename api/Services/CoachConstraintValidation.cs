using Tempo.Api.Models;

namespace Tempo.Api.Services;

public static class CoachConstraintKinds
{
    public const string SecondaryRace = "secondary_race";
    public const string Blackout = "blackout";
    public const string StandingSchedule = "standing_schedule";

    public static readonly HashSet<string> All = new(StringComparer.Ordinal)
    {
        SecondaryRace,
        Blackout,
        StandingSchedule
    };
}

public static class CoachConstraintIntents
{
    public const string TuneUp = "tune_up";
    public const string AllOut = "all_out";
    public const string FunRun = "fun_run";

    public static readonly HashSet<string> All = new(StringComparer.Ordinal)
    {
        TuneUp,
        AllOut,
        FunRun
    };
}

public static class CoachStandingPresets
{
    public const string LongDay = "long_day";
    public const string NoQualityOn = "no_quality_on";
    public const string NoRunOn = "no_run_on";

    public static readonly HashSet<string> All = new(StringComparer.Ordinal)
    {
        LongDay,
        NoQualityOn,
        NoRunOn
    };
}

/// <summary>
/// Validates and normalizes CoachConstraint create/update payloads.
/// </summary>
public static class CoachConstraintValidation
{
    public sealed record NormalizedConstraint(
        string Kind,
        DateOnly? StartDate,
        DateOnly? EndDate,
        string? Distance,
        double? DistanceM,
        string? Intent,
        string? Preset,
        List<int>? Weekdays,
        string? Note);

    public static string? TryNormalize(
        string? kind,
        string? startDate,
        string? endDate,
        string? distance,
        string? intent,
        string? preset,
        IReadOnlyList<int>? weekdays,
        string? note,
        out NormalizedConstraint? normalized)
    {
        normalized = null;
        var kindKey = kind?.Trim().ToLowerInvariant();
        if (kindKey == null || !CoachConstraintKinds.All.Contains(kindKey))
        {
            return "kind must be secondary_race, blackout, or standing_schedule";
        }

        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote != null && trimmedNote.Length > 500)
        {
            return "note must be at most 500 characters";
        }

        return kindKey switch
        {
            CoachConstraintKinds.SecondaryRace => NormalizeSecondaryRace(
                startDate, distance, intent, trimmedNote, out normalized),
            CoachConstraintKinds.Blackout => NormalizeBlackout(
                startDate, endDate, trimmedNote, out normalized),
            CoachConstraintKinds.StandingSchedule => NormalizeStanding(
                preset, weekdays, trimmedNote, out normalized),
            _ => "kind must be secondary_race, blackout, or standing_schedule"
        };
    }

    private static string? NormalizeSecondaryRace(
        string? startDate,
        string? distance,
        string? intent,
        string? note,
        out NormalizedConstraint? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(startDate) || !DateOnly.TryParse(startDate, out var date))
        {
            return "startDate is required for secondary_race (yyyy-MM-dd)";
        }

        if (!CoachGoalDistances.TryResolve(distance, out var distanceLabel, out var distanceM))
        {
            return "Unknown secondary_race distance; use a 5K–marathon catalog distance";
        }

        var intentKey = string.IsNullOrWhiteSpace(intent)
            ? CoachConstraintIntents.TuneUp
            : intent.Trim().ToLowerInvariant();
        if (!CoachConstraintIntents.All.Contains(intentKey))
        {
            return "intent must be tune_up, all_out, or fun_run";
        }

        normalized = new NormalizedConstraint(
            Kind: CoachConstraintKinds.SecondaryRace,
            StartDate: date,
            EndDate: null,
            Distance: distanceLabel,
            DistanceM: distanceM,
            Intent: intentKey,
            Preset: null,
            Weekdays: null,
            Note: note);
        return null;
    }

    private static string? NormalizeBlackout(
        string? startDate,
        string? endDate,
        string? note,
        out NormalizedConstraint? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(startDate) || !DateOnly.TryParse(startDate, out var start))
        {
            return "startDate is required for blackout (yyyy-MM-dd)";
        }

        DateOnly? end = null;
        if (!string.IsNullOrWhiteSpace(endDate))
        {
            if (!DateOnly.TryParse(endDate, out var parsedEnd))
            {
                return "endDate must be yyyy-MM-dd when provided";
            }

            if (parsedEnd < start)
            {
                return "endDate must be on or after startDate";
            }

            end = parsedEnd;
        }

        normalized = new NormalizedConstraint(
            Kind: CoachConstraintKinds.Blackout,
            StartDate: start,
            EndDate: end,
            Distance: null,
            DistanceM: null,
            Intent: null,
            Preset: null,
            Weekdays: null,
            Note: note);
        return null;
    }

    private static string? NormalizeStanding(
        string? preset,
        IReadOnlyList<int>? weekdays,
        string? note,
        out NormalizedConstraint? normalized)
    {
        normalized = null;
        var presetKey = preset?.Trim().ToLowerInvariant();
        if (presetKey == null || !CoachStandingPresets.All.Contains(presetKey))
        {
            return "Unknown standing_schedule preset; use long_day, no_quality_on, or no_run_on";
        }

        if (weekdays == null || weekdays.Count == 0)
        {
            return "weekdays are required for standing_schedule (ISO 1=Mon … 7=Sun)";
        }

        var unique = weekdays.Distinct().OrderBy(d => d).ToList();
        if (unique.Count != weekdays.Count)
        {
            return "weekdays must not contain duplicates";
        }

        if (unique.Any(d => d is < 1 or > 7))
        {
            return "weekdays must be ISO values 1–7 (Mon–Sun)";
        }

        if (presetKey == CoachStandingPresets.LongDay && unique.Count != 1)
        {
            return "long_day requires exactly one weekday";
        }

        normalized = new NormalizedConstraint(
            Kind: CoachConstraintKinds.StandingSchedule,
            StartDate: null,
            EndDate: null,
            Distance: null,
            DistanceM: null,
            Intent: null,
            Preset: presetKey,
            Weekdays: unique,
            Note: note);
        return null;
    }

    public static void Apply(CoachConstraint row, NormalizedConstraint n, DateTime now, bool isCreate)
    {
        row.Kind = n.Kind;
        row.StartDate = n.StartDate;
        row.EndDate = n.EndDate;
        row.Distance = n.Distance;
        row.DistanceM = n.DistanceM;
        row.Intent = n.Intent;
        row.Preset = n.Preset;
        row.Weekdays = n.Weekdays;
        row.Note = n.Note;
        row.UpdatedAt = now;
        if (isCreate)
        {
            row.CreatedAt = now;
            row.IsRetired = false;
            row.RetiredAt = null;
        }
    }
}
