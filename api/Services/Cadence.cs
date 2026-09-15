namespace Tempo.Api.Services;

/// <summary>
/// Running cadence unit conversion. FIT record/session cadence is strides/min;
/// Tempo stores steps/min (both feet) in CadenceRpm columns.
/// </summary>
public static class Cadence
{
    /// <summary>
    /// Converts FIT strides/min to steps/min. Null stays null; clamps at 255.
    /// </summary>
    public static byte? StepsPerMinuteFromFit(byte? fitCadence)
    {
        if (!fitCadence.HasValue)
        {
            return null;
        }

        return (byte)Math.Min(255, fitCadence.Value * 2);
    }
}
