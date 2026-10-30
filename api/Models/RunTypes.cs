namespace Tempo.Api.Models;

/// <summary>
/// The accepted <see cref="Workout.RunType"/> values. Single source for validation, the import default,
/// stats rules, and the OpenAPI enum.
/// </summary>
public static class RunTypes
{
    public const string EasyRun = "Easy Run";
    public const string Recovery = "Recovery";
    public const string WalkRun = "Walk-Run";
    public const string LongRun = "Long Run";
    public const string Workout = "Workout";
    public const string Threshold = "Threshold";
    public const string Intervals = "Intervals";
    public const string Hills = "Hills";
    public const string Fartlek = "Fartlek";
    public const string Race = "Race";

    /// <summary>
    /// Every accepted value, in display order. Matching is ordinal (case-sensitive).
    /// </summary>
    public static readonly IReadOnlyList<string> All =
    [
        EasyRun, Recovery, WalkRun, LongRun, Workout, Threshold, Intervals, Hills, Fartlek, Race,
    ];

    public static bool IsValid(string value) => All.Contains(value, StringComparer.Ordinal);
}
