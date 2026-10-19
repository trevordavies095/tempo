using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Tempo.Api.Services;

/// <summary>
/// Strips athlete identity and GPS/route fields from a coach context packet before provider send.
/// Also scrubs username / email patterns from free-text leaves (memory summaries, notes, rules).
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
        out string? error,
        CoachRedactionContext? context = null)
    {
        redactedJson = string.Empty;
        report = new CoachPrivacyReport([], [], [], []);
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
            RedactNode(node, stripped, context);

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
            report = new CoachPrivacyReport([], [], [], []);
            return false;
        }
    }

    private static void RedactNode(JsonNode? node, HashSet<string> stripped, CoachRedactionContext? context)
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

                var child = obj[key];
                if (child is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    var scrubbed = ScrubString(text, context, stripped);
                    if (!ReferenceEquals(scrubbed, text) && scrubbed != text)
                    {
                        obj[key] = scrubbed;
                    }
                }
                else
                {
                    RedactNode(child, stripped, context);
                }
            }
        }
        else if (node is JsonArray arr)
        {
            for (var i = 0; i < arr.Count; i++)
            {
                var child = arr[i];
                if (child is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    var scrubbed = ScrubString(text, context, stripped);
                    if (!ReferenceEquals(scrubbed, text) && scrubbed != text)
                    {
                        arr[i] = scrubbed;
                    }
                }
                else
                {
                    RedactNode(child, stripped, context);
                }
            }
        }
    }

    internal static string ScrubString(string text, CoachRedactionContext? context, HashSet<string> stripped)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var result = text;
        if (!string.IsNullOrWhiteSpace(context?.Username)
            && result.Contains(context.Username, StringComparison.OrdinalIgnoreCase))
        {
            result = Regex.Replace(
                result,
                Regex.Escape(context.Username),
                "[redacted]",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            stripped.Add("athleteIdentity");
        }

        if (!string.IsNullOrWhiteSpace(context?.Email)
            && result.Contains(context.Email, StringComparison.OrdinalIgnoreCase))
        {
            result = Regex.Replace(
                result,
                Regex.Escape(context.Email),
                "[redacted]",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            stripped.Add("athleteIdentity");
        }

        var emailScrubbed = EmailRegex().Replace(result, match =>
        {
            stripped.Add("athleteIdentity");
            return "[redacted]";
        });
        result = emailScrubbed;

        return result;
    }

    private static bool IsForbiddenKey(string key)
    {
        if (ForbiddenKeys.Contains(key))
        {
            return true;
        }

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
        var selectedEpisodeIds = new List<string>();
        var selectedRuleIds = new List<string>();
        var layer2Detail = "Layer 2 memory (empty until later slices)";
        var layer3Detail = "Layer 3 rules (empty until later slices)";

        var recentTrainingDetail =
            "Recent Tempo training weeks (empty until assembled)";

        if (packet is CoachContextPacket typed)
        {
            selectedEpisodeIds.AddRange(typed.Layer2.PriorWeek.Select(e => e.Id));
            selectedEpisodeIds.AddRange(typed.Layer2.Retrieved.Select(e => e.Id));
            selectedRuleIds.AddRange(typed.Layer3.Rules.Select(r => r.Id));

            var selected = selectedEpisodeIds.Count;
            var total = typed.Layer2.TotalEpisodeCount > 0
                ? typed.Layer2.TotalEpisodeCount
                : selected;
            layer2Detail =
                $"{selected} episodes selected of {total} — text you accepted or wrote, scrubbed of name/email/coordinates";
            layer3Detail =
                $"{selectedRuleIds.Count} active standing rules — text you accepted or wrote, scrubbed of name/email/coordinates";

            var weeks = typed.RecentTraining.Weeks;
            if (weeks.Count > 0)
            {
                var runCount = weeks.Sum(w => w.RunCount);
                var start = weeks[0].WeekStartDate;
                var end = weeks[^1].WeekEndDate;
                recentTrainingDetail =
                    $"{weeks.Count} Mon–Sun weeks ({start}→{end}), {runCount} runs — distance/duration/HR/RPE/shoe ids and notes; no titles or GPS";
            }
            else
            {
                recentTrainingDetail = "0 recent-training weeks";
            }

            var exchangeCount = typed.CheckInWeekExchanges?.Count ?? 0;
            var checkInExchangesDetail =
                $"{exchangeCount} conversations from this week (athlete text ≤400 chars, verdict, short coach summary)";

            var weeklyIncluded = new List<CoachPrivacyReportItem>
            {
                new("meta", "Week bounds, unit preference, assemble timestamp"),
                new("goal", "Primary CoachGoal fields"),
                new("constraints", "Active CoachConstraint non-negotiables"),
                new("layer1", "HR zones, paces, shoes, run days per week, training phase (no athlete name)"),
                new("layer2", layer2Detail),
                new("layer3", layer3Detail),
                new("checkIn", "Subjective fields and run metrics/notes without workout names or GPS"),
                new("recentTraining", recentTrainingDetail),
                new("checkInWeekExchanges", checkInExchangesDetail),
                new("adjustment", "Athlete-started mid-week adjust reason, category, and locked/remaining days"),
                new("athleteFeedback", "Athlete note requesting draft changes plus previous-draft day summary")
            };

            var weeklyStripped = new List<CoachPrivacyReportItem>
            {
                new("athleteIdentity", "Name, email, and username fields"),
                new("workoutNames", "Workout display names (never assembled into provider packet)"),
                new("gpsRoutes", "GPS coordinates, routes, and GeoJSON"),
                new("hrStreams", "Heart-rate time series (never assembled into provider packet)"),
                new("similarRoutes", "Similar-route analysis (never assembled into provider packet)"),
                new("rawFiles", "Raw GPX/FIT/Strava/HealthKit blobs")
            };

            foreach (var cat in stripped)
            {
                if (weeklyStripped.All(s => !string.Equals(s.Category, cat, StringComparison.OrdinalIgnoreCase)))
                {
                    weeklyStripped.Add(new CoachPrivacyReportItem(cat, "Removed during redaction"));
                }
            }

            return new CoachPrivacyReport(
                weeklyIncluded,
                weeklyStripped,
                selectedEpisodeIds.Distinct(StringComparer.Ordinal).ToList(),
                selectedRuleIds.Distinct(StringComparer.Ordinal).ToList());
        }

        if (packet is CoachExchangePacket exchange)
        {
            selectedRuleIds.AddRange(exchange.Layer3.Rules.Select(r => r.Id));
            layer3Detail =
                $"{selectedRuleIds.Count} active standing rules — text you accepted or wrote, scrubbed of name/email/coordinates";

            var weeks = exchange.RecentTraining.Weeks;
            if (weeks.Count > 0)
            {
                var runCount = weeks.Sum(w => w.RunCount);
                var start = weeks[0].WeekStartDate;
                var end = weeks[^1].WeekEndDate;
                recentTrainingDetail =
                    $"{weeks.Count} Mon–Sun weeks ({start}→{end}), {runCount} runs — distance/duration/HR/RPE/shoe ids and notes; no titles or GPS";
            }
            else
            {
                recentTrainingDetail = "0 recent-training weeks";
            }

            var planDetail = exchange.CommittedPlanSlice == null
                ? "No committed plan for this week"
                : $"{exchange.CommittedPlanSlice.Count} planned days (day type, distance, structure, pace/HR, shoe) — no titles or GPS";
            var exchangesDetail =
                $"{exchange.WeekExchanges.Count} prior exchanges this week (athlete text ≤400 chars, verdict, short coach summary)";
            var athleteTextDetail = string.IsNullOrWhiteSpace(exchange.AthleteText)
                ? "Empty athlete message"
                : "Your message to the coach (avoid names and addresses)";
            string attachedRunDetail;
            if (exchange.AttachedRun == null)
            {
                attachedRunDetail = "No attached run";
            }
            else
            {
                var ar = exchange.AttachedRun;
                var plannedBit = ar.Planned == null ? "no planned session" : "planned session + adherence hint";
                attachedRunDetail =
                    $"Attached run: core stats, {ar.Splits.Count} splits, {ar.Series.Count} series points (HR/pace/cadence/elev/grade), ambient weather, {plannedBit} — no GPS, name, or device temperature";
            }

            var exchangeIncluded = new List<CoachPrivacyReportItem>
            {
                new("meta", "Week bounds, unit preference, assemble timestamp"),
                new("goal", "Primary CoachGoal fields"),
                new("constraints", "Active CoachConstraint non-negotiables"),
                new("layer1", "HR zones, paces, shoes, run days per week, training phase (no athlete name)"),
                new("layer3", layer3Detail),
                new("committedPlanSlice", planDetail),
                new("weekExchanges", exchangesDetail),
                new("athleteText", athleteTextDetail),
                new("recentTraining", recentTrainingDetail),
                new("attachedRun", attachedRunDetail)
            };

            var exchangeStripped = new List<CoachPrivacyReportItem>
            {
                new("athleteIdentity", "Name, email, and username fields"),
                new("workoutNames", "Workout display names (never assembled into provider packet)"),
                new("gpsRoutes", "GPS coordinates, routes, and GeoJSON"),
                new("deviceTemperature", "Device/skin temperature (never assembled)"),
                new("rawFiles", "Raw GPX/FIT/Strava/HealthKit blobs")
            };

            foreach (var cat in stripped)
            {
                if (exchangeStripped.All(s => !string.Equals(s.Category, cat, StringComparison.OrdinalIgnoreCase)))
                {
                    exchangeStripped.Add(new CoachPrivacyReportItem(cat, "Removed during redaction"));
                }
            }

            return new CoachPrivacyReport(
                exchangeIncluded,
                exchangeStripped,
                selectedEpisodeIds.Distinct(StringComparer.Ordinal).ToList(),
                selectedRuleIds.Distinct(StringComparer.Ordinal).ToList());
        }

        var included = new List<CoachPrivacyReportItem>
        {
            new("meta", "Week bounds, unit preference, assemble timestamp"),
            new("goal", "Primary CoachGoal fields"),
            new("constraints", "Active CoachConstraint non-negotiables"),
            new("layer1", "HR zones, paces, shoes, run days per week, training phase (no athlete name)"),
            new("layer2", layer2Detail),
            new("layer3", layer3Detail),
            new("checkIn", "Subjective fields and run metrics/notes without workout names or GPS"),
            new("recentTraining", recentTrainingDetail),
            new("adjustment", "Athlete-started mid-week adjust reason, category, and locked/remaining days"),
            new("athleteFeedback", "Athlete note requesting draft changes plus previous-draft day summary")
        };

        var strippedItems = new List<CoachPrivacyReportItem>
        {
            new("athleteIdentity", "Name, email, and username fields"),
            new("workoutNames", "Workout display names (never assembled into provider packet)"),
            new("gpsRoutes", "GPS coordinates, routes, and GeoJSON"),
            new("hrStreams", "Heart-rate time series (never assembled into provider packet)"),
            new("similarRoutes", "Similar-route analysis (never assembled into provider packet)"),
            new("rawFiles", "Raw GPX/FIT/Strava/HealthKit blobs")
        };

        foreach (var cat in stripped)
        {
            if (strippedItems.All(s => !string.Equals(s.Category, cat, StringComparison.OrdinalIgnoreCase)))
            {
                strippedItems.Add(new CoachPrivacyReportItem(cat, "Removed during redaction"));
            }
        }

        return new CoachPrivacyReport(
            included,
            strippedItems,
            selectedEpisodeIds.Distinct(StringComparer.Ordinal).ToList(),
            selectedRuleIds.Distinct(StringComparer.Ordinal).ToList());
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

    /// <summary>
    /// Extracts selected episode/rule ids from a stored redacted packet JSON.
    /// </summary>
    public static (IReadOnlyList<string> EpisodeIds, IReadOnlyList<string> RuleIds) ExtractSelectedIds(
        string? redactedPayloadJson)
    {
        var episodes = new List<string>();
        var rules = new List<string>();
        if (string.IsNullOrWhiteSpace(redactedPayloadJson))
        {
            return (episodes, rules);
        }

        try
        {
            using var doc = JsonDocument.Parse(redactedPayloadJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("layer2", out var layer2))
            {
                CollectIds(layer2, "priorWeek", episodes);
                CollectIds(layer2, "retrieved", episodes);
            }

            if (root.TryGetProperty("layer3", out var layer3)
                && layer3.TryGetProperty("rules", out var rulesArr)
                && rulesArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in rulesArr.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    {
                        var value = id.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            rules.Add(value);
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Caller gets empty lists when payload is unusable.
        }

        return (
            episodes.Distinct(StringComparer.Ordinal).ToList(),
            rules.Distinct(StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// Extracts checkInWeekExchanges count and exchange ids from redacted weekly packet JSON.
    /// </summary>
    public static (int ExchangeCount, IReadOnlyList<string> ExchangeIds) ExtractCheckInWeekExchangesSummary(
        string? redactedPayloadJson)
    {
        if (string.IsNullOrWhiteSpace(redactedPayloadJson))
        {
            return (0, []);
        }

        try
        {
            using var doc = JsonDocument.Parse(redactedPayloadJson);
            if (!doc.RootElement.TryGetProperty("checkInWeekExchanges", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
            {
                return (0, []);
            }

            var ids = new List<string>();
            foreach (var item in arr.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idEl))
                {
                    continue;
                }

                var id = idEl.ValueKind == JsonValueKind.String
                    ? idEl.GetString()
                    : idEl.ToString();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    ids.Add(id);
                }
            }

            return (arr.GetArrayLength(), ids);
        }
        catch (JsonException)
        {
            return (0, []);
        }
    }

    /// <summary>
    /// Extracts recentTraining week ranges, run counts, and workout ids from redacted packet JSON.
    /// </summary>
    public static IReadOnlyList<CoachRecentTrainingWeekReceipt> ExtractRecentTrainingSummary(
        string? redactedPayloadJson)
    {
        var weeks = new List<CoachRecentTrainingWeekReceipt>();
        if (string.IsNullOrWhiteSpace(redactedPayloadJson))
        {
            return weeks;
        }

        try
        {
            using var doc = JsonDocument.Parse(redactedPayloadJson);
            if (!doc.RootElement.TryGetProperty("recentTraining", out var recent)
                || !recent.TryGetProperty("weeks", out var weeksArr)
                || weeksArr.ValueKind != JsonValueKind.Array)
            {
                return weeks;
            }

            foreach (var week in weeksArr.EnumerateArray())
            {
                var start = week.TryGetProperty("weekStartDate", out var s) ? s.GetString() ?? "" : "";
                var end = week.TryGetProperty("weekEndDate", out var e) ? e.GetString() ?? "" : "";
                var runCount = week.TryGetProperty("runCount", out var rc) && rc.TryGetInt32(out var n) ? n : 0;
                var workoutIds = new List<string>();
                if (week.TryGetProperty("runs", out var runs) && runs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var run in runs.EnumerateArray())
                    {
                        if (run.TryGetProperty("workoutId", out var idEl))
                        {
                            var id = idEl.ValueKind == JsonValueKind.String
                                ? idEl.GetString()
                                : idEl.ToString();
                            if (!string.IsNullOrWhiteSpace(id))
                            {
                                workoutIds.Add(id);
                            }
                        }
                    }
                }

                weeks.Add(new CoachRecentTrainingWeekReceipt(start, end, runCount, workoutIds));
            }
        }
        catch (JsonException)
        {
            // Caller gets empty list when payload is unusable.
        }

        return weeks;
    }

    private static void CollectIds(JsonElement parent, string arrayName, List<string> sink)
    {
        if (!parent.TryGetProperty(arrayName, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in arr.EnumerateArray())
        {
            if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            {
                var value = id.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    sink.Add(value);
                }
            }
        }
    }

    [GeneratedRegex(@"\[\s*-?\d{1,3}\.\d+\s*,\s*-?\d{1,3}\.\d+\s*\]", RegexOptions.CultureInvariant)]
    private static partial Regex CoordinatePairRegex();

    [GeneratedRegex(@"""(lat|lng|lon|latitude|longitude)""\s*:\s*-?\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LatLngPropertyRegex();

    [GeneratedRegex(@"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();
}

public sealed record CoachRedactionContext(string? Username, string? Email = null);

public sealed record CoachPrivacyReport(
    IReadOnlyList<CoachPrivacyReportItem> Included,
    IReadOnlyList<CoachPrivacyReportItem> Stripped,
    IReadOnlyList<string> SelectedEpisodeIds,
    IReadOnlyList<string> SelectedRuleIds);

public sealed record CoachPrivacyReportItem(string Category, string Detail);

public sealed record CoachRecentTrainingWeekReceipt(
    string WeekStartDate,
    string WeekEndDate,
    int RunCount,
    IReadOnlyList<string> WorkoutIds);
