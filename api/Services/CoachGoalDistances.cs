namespace Tempo.Api.Services;

/// <summary>
/// Road 5K–marathon distance catalog for CoachGoal (subset of BestEffortService.StandardDistances).
/// </summary>
public static class CoachGoalDistances
{
    public static readonly IReadOnlyDictionary<string, double> Catalog =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["5K"] = 5000,
            ["10K"] = 10000,
            ["15K"] = 15000,
            ["10 mile"] = 16093.44,
            ["20K"] = 20000,
            ["Half-Marathon"] = 21097.5,
            ["30K"] = 30000,
            ["Marathon"] = 42195
        };

    public static bool TryResolve(string? distance, out string label, out double distanceM)
    {
        label = string.Empty;
        distanceM = 0;
        if (string.IsNullOrWhiteSpace(distance))
        {
            return false;
        }

        var key = distance.Trim();
        if (!Catalog.TryGetValue(key, out distanceM))
        {
            // Case-insensitive match for UX, persist canonical catalog key.
            var match = Catalog.Keys.FirstOrDefault(k =>
                string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                return false;
            }

            key = match;
            distanceM = Catalog[key];
        }

        label = key;
        return true;
    }
}
