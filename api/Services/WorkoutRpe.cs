namespace Tempo.Api.Services;

/// <summary>
/// FIT session <c>workout_rpe</c> is Borg CR10 stored × 10. Tempo <see cref="Models.Workout.Rpe"/> is 1–10.
/// </summary>
public static class WorkoutRpe
{
    /// <summary>
    /// Decode raw FIT session workoutRpe to Tempo 1–10. Accepts exact tens in 10–100; otherwise null.
    /// </summary>
    public static byte? FromFitSessionRaw(int? raw)
    {
        if (!raw.HasValue)
        {
            return null;
        }

        var value = raw.Value;
        if (value < 10 || value > 100 || value % 10 != 0)
        {
            return null;
        }

        return (byte)(value / 10);
    }
}
