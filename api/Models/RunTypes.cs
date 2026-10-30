namespace Tempo.Api.Models;

/// <summary>
/// The accepted <see cref="Workout.RunType"/> values. Single source for validation, the import default,
/// stats rules, and the OpenAPI enum.
/// </summary>
public static class RunTypes
{
    public const string EasyRun = "Easy Run";
    public const string LongRun = "Long Run";
    public const string Workout = "Workout";
    public const string Race = "Race";

    /// <summary>
    /// Every accepted value, in display order. Matching is ordinal (case-sensitive).
    /// </summary>
    public static readonly IReadOnlyList<string> All = [EasyRun, LongRun, Workout, Race];

    public static bool IsValid(string value) => All.Contains(value, StringComparer.Ordinal);
}
