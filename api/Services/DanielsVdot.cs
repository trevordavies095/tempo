namespace Tempo.Api.Services;

/// <summary>
/// Jack Daniels' VDOT from a race effort (Daniels–Gilbert oxygen-cost and drop-dead equations), and the
/// threshold (T) pace it implies: the velocity whose oxygen cost is <see cref="ThresholdFraction"/> of VDOT.
/// </summary>
public static class DanielsVdot
{
    /// <summary>
    /// Fraction of VDOT run at threshold (T) pace
    /// </summary>
    public const double ThresholdFraction = 0.88;

    /// <summary>
    /// VDOT for covering <paramref name="distanceM"/> in <paramref name="timeS"/>
    /// </summary>
    public static double Vdot(double distanceM, double timeS)
    {
        if (distanceM <= 0 || timeS <= 0)
        {
            throw new ArgumentException("Distance and time must be positive");
        }

        var minutes = timeS / 60.0;
        var velocity = distanceM / minutes; // m/min
        var fractionOfMax = 0.8
            + 0.1894393 * Math.Exp(-0.012778 * minutes)
            + 0.2989558 * Math.Exp(-0.1932605 * minutes);
        return OxygenCost(velocity) / fractionOfMax;
    }

    /// <summary>
    /// Threshold pace in seconds per km for a given VDOT
    /// </summary>
    public static double ThresholdPaceSecondsPerKm(double vdot)
    {
        // Solve 0.000104 v^2 + 0.182258 v - 4.60 = ThresholdFraction * vdot for v (m/min)
        var target = ThresholdFraction * vdot;
        const double a = 0.000104;
        const double b = 0.182258;
        var c = -4.60 - target;
        var velocity = (-b + Math.Sqrt(b * b - 4 * a * c)) / (2 * a);
        return 1000.0 / velocity * 60.0;
    }

    // ml O2/kg/min at velocity in m/min
    private static double OxygenCost(double velocity) =>
        -4.60 + 0.182258 * velocity + 0.000104 * velocity * velocity;
}
