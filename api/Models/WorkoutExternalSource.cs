namespace Tempo.Api.Models;

/// <summary>
/// Upstream tokens for <see cref="WorkoutExternalIdentity.Source"/>.
/// Not the same vocabulary as <see cref="Workout.Source"/> (ingest provenance).
/// </summary>
public static class WorkoutExternalSource
{
    public const string IntervalsIcu = "intervals_icu";
}
