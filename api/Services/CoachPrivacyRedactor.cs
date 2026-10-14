using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Tempo.Api.Services;

/// <summary>
/// Strips athlete identity and GPS/route fields from a coach context packet before provider send.
/// Fail-closed: if redaction cannot complete or residual GPS-looking data remains, returns false.
/// </summary>
public static partial class CoachPrivacyRedactor
{
    private static readonly HashSet<string> ForbiddenKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "name",
        "email",
        "username",
        "lat",
        "lng",
        "lon",
        "latitude",
        "longitude",
        "coordinates",
        "route",
        "geojson",
        "geoJson",
        "routeGeoJson",
        "previewGeoJson",
        "rawGpxData",
        "rawFitData",
        "rawStravaData",
        "rawHealthKitData"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Redacts a packet object. On success, <paramref name="redactedJson"/> is compact camelCase JSON
    /// and <paramref name="report"/> describes included vs stripped categories.
    /// </summary>
    public static bool TryRedact(
        object packet,
        out string redactedJson,
        out CoachPrivacyReport report,
        out string? error)
    {
        redactedJson = string.Empty;
        report = new CoachPrivacyReport([], []);
        error = null;

        try
        {
            var json = JsonSerializer.Serialize(packet, JsonOptions);
            var node = JsonNode.Parse(json);
            if (node == null)
            {
                error = "Packet could not be parsed for redaction";
                return false;
            }

            var stripped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            RedactNode(node, stripped);

            redactedJson = node.ToJsonString(JsonOptions);

            if (LooksLikeResidualGps(redactedJson))
            {
                error = "Redaction failed: residual GPS-looking coordinates remain in packet";
                redactedJson = string.Empty;
                return false;
            }

            report = BuildReport(packet, stripped);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Redaction failed: {ex.Message}";
            redactedJson = string.Empty;
            report = new CoachPrivacyReport([], []);
            return false;
        }
    }

    private static void RedactNode(JsonNode? node, HashSet<string> stripped)
    {
        if (node is JsonObject obj)
        {
            var keys = obj.Select(p => p.Key).ToList();
            foreach (var key in keys)
            {
                if (IsForbiddenKey(key))
                {
                    obj.Remove(key);
                    stripped.Add(CategoryForKey(key));
                    continue;
                }

                RedactNode(obj[key], stripped);
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var child in arr)
            {
                RedactNode(child, stripped);
            }
        }
    }

    private static bool IsForbiddenKey(string key)
    {
        if (ForbiddenKeys.Contains(key))
        {
            return true;
        }

        // Catch geoJson / routeGeoJson variants normalized
        var normalized = key.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
        return normalized is "geojson" or "routegojson" or "previewgeojson"
            or "rawgpxdata" or "rawfitdata" or "rawstravadata" or "rawhealthkitdata";
    }

    private static string CategoryForKey(string key)
    {
        var k = key.ToLowerInvariant();
        if (k is "name" or "email" or "username")
        {
            return "athleteIdentity";
        }

        if (k.Contains("raw", StringComparison.OrdinalIgnoreCase))
        {
            return "rawFiles";
        }

        return "gpsRoutes";
    }

    private static CoachPrivacyReport BuildReport(object packet, HashSet<string> stripped)
    {
        var included = new List<CoachPrivacyReportItem>
        {
            new("meta", "Week bounds, unit preference, assemble timestamp"),
            new("goal", "Primary CoachGoal fields"),
            new("constraints", "Active CoachConstraint non-negotiables"),
            new("layer1", "HR zones, paces, shoes (no athlete name)"),
            new("layer2", "Layer 2 memory (empty until later slices)"),
            new("layer3", "Layer 3 rules (empty until later slices)"),
            new("checkIn", "Subjective fields and run metrics/notes without workout names or GPS"),
            new("adjustment", "Athlete-started mid-week adjust reason, category, and locked/remaining days")
        };

        // Always advertise what we strip by policy, even if keys were absent in this assemble.
        var strippedItems = new List<CoachPrivacyReportItem>
        {
            new("athleteIdentity", "Name, email, and username fields"),
            new("workoutNames", "Workout display names (never assembled into provider packet)"),
            new("gpsRoutes", "GPS coordinates, routes, and GeoJSON"),
            new("rawFiles", "Raw GPX/FIT/Strava/HealthKit blobs")
        };

        foreach (var cat in stripped)
        {
            if (strippedItems.All(s => !string.Equals(s.Category, cat, StringComparison.OrdinalIgnoreCase)))
            {
                strippedItems.Add(new CoachPrivacyReportItem(cat, "Removed during redaction"));
            }
        }

        // Keep compiler happy if packet shape is unused for inclusion detection later
        _ = packet;

        return new CoachPrivacyReport(included, strippedItems);
    }

    /// <summary>
    /// Detects residual lat/lng pair patterns like [42.3, -71.1] or "lat":42.
    /// </summary>
    public static bool LooksLikeResidualGps(string json)
    {
        if (CoordinatePairRegex().IsMatch(json))
        {
            return true;
        }

        if (LatLngPropertyRegex().IsMatch(json))
        {
            return true;
        }

        return false;
    }

    [GeneratedRegex(@"\[\s*-?\d{1,3}\.\d+\s*,\s*-?\d{1,3}\.\d+\s*\]", RegexOptions.CultureInvariant)]
    private static partial Regex CoordinatePairRegex();

    [GeneratedRegex(@"""(lat|lng|lon|latitude|longitude)""\s*:\s*-?\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LatLngPropertyRegex();
}

public sealed record CoachPrivacyReport(
    IReadOnlyList<CoachPrivacyReportItem> Included,
    IReadOnlyList<CoachPrivacyReportItem> Stripped);

public sealed record CoachPrivacyReportItem(string Category, string Detail);
