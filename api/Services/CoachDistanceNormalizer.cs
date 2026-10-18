using System.Globalization;
using System.Text.RegularExpressions;

namespace Tempo.Api.Services;

/// <summary>
/// Normalizes planned coach distances so approximate mile encodings (e.g. 4800 ≈ 3 mi)
/// and redundant leading distance in <c>structure</c> do not confuse the athlete.
/// </summary>
public static partial class CoachDistanceNormalizer
{
    public const double MetersPerMile = 1609.344;
    public const double RoundKmToleranceM = 25;
    public const double HalfMileSnapToleranceMi = 0.05;

    /// <summary>
    /// Snap planned meters toward clear coach intent: keep round-km prescriptions,
    /// otherwise snap values within <see cref="HalfMileSnapToleranceMi"/> of a half-mile.
    /// </summary>
    public static double NormalizePlannedDistanceM(double distanceM)
    {
        if (distanceM <= 0)
        {
            return distanceM;
        }

        // Preserve 5k / 8k / 10k-style prescriptions.
        var nearestKm = Math.Round(distanceM / 1000.0) * 1000.0;
        if (nearestKm >= 1000 && Math.Abs(distanceM - nearestKm) <= RoundKmToleranceM)
        {
            return nearestKm;
        }

        var miles = distanceM / MetersPerMile;
        var nearestHalfMile = Math.Round(miles * 2.0) / 2.0;
        if (nearestHalfMile >= 0.5 && Math.Abs(miles - nearestHalfMile) <= HalfMileSnapToleranceMi)
        {
            return nearestHalfMile * MetersPerMile;
        }

        return distanceM;
    }

    /// <summary>
    /// Drop a leading total-distance phrase from structure when it restates <paramref name="distanceM"/>.
    /// Leaves interval recipes like "2mi WU + 2mi @ tempo" alone.
    /// </summary>
    public static string? StripRedundantLeadingDistance(string? structure, double distanceM)
    {
        if (string.IsNullOrWhiteSpace(structure) || distanceM <= 0)
        {
            return string.IsNullOrWhiteSpace(structure) ? null : structure.Trim();
        }

        var trimmed = structure.Trim();
        var match = LeadingDistanceRegex().Match(trimmed);
        if (!match.Success)
        {
            return trimmed;
        }

        if (!double.TryParse(
                match.Groups["n"].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var amount)
            || amount < 0)
        {
            return trimmed;
        }

        var unit = match.Groups["u"].Value;
        var leadingM = unit.StartsWith("mi", StringComparison.OrdinalIgnoreCase)
            ? amount * MetersPerMile
            : amount * 1000.0;

        if (!DistancesMatch(leadingM, distanceM))
        {
            return trimmed;
        }

        var rest = trimmed[match.Length..].TrimStart();
        return string.IsNullOrWhiteSpace(rest) ? null : rest;
    }

    public static void NormalizeCommitRun(CoachWeeklyCommitRun run)
    {
        run.DistanceM = NormalizePlannedDistanceM(run.DistanceM);
        run.Structure = StripRedundantLeadingDistance(run.Structure, run.DistanceM);
    }

    private static bool DistancesMatch(double aM, double bM)
    {
        var avg = (Math.Abs(aM) + Math.Abs(bM)) / 2.0;
        if (avg < 1)
        {
            return Math.Abs(aM - bM) < 1;
        }

        // ~80 m ≈ 0.05 mi — same band as half-mile snap.
        return Math.Abs(aM - bM) <= Math.Max(80, avg * 0.03);
    }

    [GeneratedRegex(
        @"^(?<n>\d+(?:\.\d+)?)\s*(?<u>mi|miles?|km|kilometers?)\b(?:\s*[·•,:\-–—]+|\s+)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeadingDistanceRegex();
}
