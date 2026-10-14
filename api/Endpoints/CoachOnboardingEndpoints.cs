using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;

namespace Tempo.Api.Endpoints;

public static class CoachOnboardingEndpoints
{
    public const string Layer1Key = "L1";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/onboarding", GetOnboarding)
            .WithName("GetCoachOnboarding")
            .Produces<CoachOnboardingDocument>(200)
            .Produces(404)
            .WithSummary("Get Coach onboarding status, goal, and Layer 1")
            .WithDescription(
                "Returns whether coach onboarding is completed (goal + L1 present), " +
                "the primary CoachGoal, and the Layer 1 document. " +
                "404 when Coach is unavailable or the athlete has not opted in.");

        group.MapGet("/onboarding/prefill", GetOnboardingPrefill)
            .WithName("GetCoachOnboardingPrefill")
            .Produces<CoachOnboardingPrefillDocument>(200)
            .Produces(400)
            .Produces(404)
            .WithSummary("Prefill Coach onboarding from Tempo")
            .WithDescription(
                "Returns HR zones, shoes, best efforts, unit preference, and suggested paces from Tempo. " +
                "Suggested paces prefer Race workouts from the last 120 days (Riegel-normalized to 5K-equivalent, averaged); " +
                "falls back to best-effort 5K / 1 mile. Does not persist. Requires Tempo onboardingCompleted. " +
                "404 when Coach is unavailable or the athlete has not opted in.");

        group.MapPost("/onboarding", PostOnboarding)
            .WithName("PostCoachOnboarding")
            .Accepts<CompleteCoachOnboardingRequest>("application/json")
            .Produces<CoachOnboardingDocument>(200)
            .Produces(400)
            .Produces(404)
            .WithSummary("Complete Coach onboarding")
            .WithDescription(
                "Upserts primary CoachGoal and Layer 1 from athlete-confirmed values. " +
                "Paces must be confirmed (all four fields required). " +
                "Requires Tempo onboardingCompleted. Not medical advice.");
    }

    private static async Task<IResult> GetOnboarding(
        TempoDbContext db,
        CoachConfig coachConfig)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        return Results.Ok(await BuildOnboardingDocumentAsync(db));
    }

    private static async Task<IResult> GetOnboardingPrefill(
        ClaimsPrincipal user,
        TempoDbContext db,
        CoachConfig coachConfig,
        HeartRateZoneService zoneService)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var tempoGate = await EnsureTempoOnboardingAsync(user, db);
        if (tempoGate != null)
        {
            return tempoGate;
        }

        var settings = await db.UserSettings.AsNoTracking().FirstOrDefaultAsync();
        var hrZones = BuildHrZonesSnapshot(settings, zoneService);
        var unitPreference = string.IsNullOrWhiteSpace(settings?.UnitPreference)
            ? "metric"
            : settings.UnitPreference!;

        var shoes = await db.Shoes.AsNoTracking()
            .Where(s => !s.IsRetired)
            .OrderBy(s => s.Brand)
            .ThenBy(s => s.Model)
            .Select(s => new CoachLayer1Shoe(s.Id, s.Brand, s.Model))
            .ToListAsync();

        var defaultShoeId = settings?.DefaultShoeId;
        if (defaultShoeId != null && shoes.All(s => s.Id != defaultShoeId))
        {
            defaultShoeId = null;
        }

        var bestEfforts = await db.BestEfforts.AsNoTracking()
            .OrderBy(be => be.DistanceM)
            .Select(be => new CoachPrefillBestEffort(
                be.Distance,
                be.DistanceM,
                be.TimeS,
                be.WorkoutDate.ToString("yyyy-MM-dd")))
            .ToListAsync();

        var utcNow = DateTime.UtcNow;
        var raceCutoff = utcNow.AddDays(-CoachPaceSuggestions.RecentRaceWindowDays);
        var recentRaces = await db.Workouts.AsNoTracking()
            .Where(w =>
                w.RunType == "Race" &&
                w.StartedAt >= raceCutoff &&
                w.DistanceM >= CoachPaceSuggestions.MinRaceDistanceM &&
                w.DurationS > 0)
            .Select(w => new
            {
                w.DistanceM,
                w.DurationS,
                w.TimerTimeS,
                w.StartedAt
            })
            .ToListAsync();

        var suggested = CoachPaceSuggestions.SuggestFromRecentRaces(
            recentRaces.Select(r => (
                r.DistanceM,
                TimeS: r.TimerTimeS is > 0 ? r.TimerTimeS.Value : r.DurationS,
                r.StartedAt)),
            utcNow);

        suggested ??= CoachPaceSuggestions.SuggestFromBestEfforts(
            bestEfforts.Select(be => (be.Distance, be.DistanceM, be.TimeS)));

        return Results.Ok(new CoachOnboardingPrefillDocument(
            UnitPreference: unitPreference,
            HrZones: hrZones,
            Shoes: shoes,
            DefaultShoeId: defaultShoeId,
            BestEfforts: bestEfforts,
            SuggestedPaces: suggested,
            GoalDistances: CoachGoalDistances.Catalog.Keys.ToList(),
            Disclaimer:
                "Tempo AI Coach is not a doctor and does not provide medical advice. " +
                "It offers training suggestions only. Seek a qualified professional for pain, injury, or health concerns."));
    }

    private static async Task<IResult> PostOnboarding(
        [FromBody] CompleteCoachOnboardingRequest? request,
        ClaimsPrincipal user,
        TempoDbContext db,
        CoachConfig coachConfig,
        HeartRateZoneService zoneService)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var tempoGate = await EnsureTempoOnboardingAsync(user, db);
        if (tempoGate != null)
        {
            return tempoGate;
        }

        if (request?.Goal == null)
        {
            return Results.BadRequest(new { error = "goal is required" });
        }

        if (request.Paces == null ||
            request.Paces.EasySecPerKm <= 0 ||
            request.Paces.TempoSecPerKm <= 0 ||
            request.Paces.ThresholdSecPerKm <= 0 ||
            request.Paces.IntervalSecPerKm <= 0)
        {
            return Results.BadRequest(new { error = "Confirmed paces are required (easy, tempo, threshold, interval sec/km)" });
        }

        var goalType = request.Goal.Type?.Trim().ToLowerInvariant();
        if (goalType is not ("race" or "pr"))
        {
            return Results.BadRequest(new { error = "goal.type must be race or pr" });
        }

        if (!CoachGoalDistances.TryResolve(request.Goal.Distance, out var distanceLabel, out var distanceM))
        {
            return Results.BadRequest(new { error = "Unknown goal distance; use a 5K–marathon catalog distance" });
        }

        DateOnly? raceDate = null;
        if (goalType == "race")
        {
            if (string.IsNullOrWhiteSpace(request.Goal.RaceDate) ||
                !DateOnly.TryParse(request.Goal.RaceDate, out var parsedDate))
            {
                return Results.BadRequest(new { error = "raceDate is required for race goals (yyyy-MM-dd)" });
            }

            raceDate = parsedDate;
        }
        else if (!string.IsNullOrWhiteSpace(request.Goal.RaceDate))
        {
            if (!DateOnly.TryParse(request.Goal.RaceDate, out var parsedOptional))
            {
                return Results.BadRequest(new { error = "raceDate must be yyyy-MM-dd when provided" });
            }

            raceDate = parsedOptional;
        }

        int? targetTimeS = request.Goal.TargetTimeS;
        if (goalType == "pr")
        {
            if (targetTimeS is null or <= 0)
            {
                return Results.BadRequest(new { error = "targetTimeS is required for pr goals" });
            }
        }
        else if (targetTimeS is <= 0)
        {
            return Results.BadRequest(new { error = "targetTimeS must be positive when provided" });
        }

        var notes = string.IsNullOrWhiteSpace(request.Goal.Notes)
            ? null
            : request.Goal.Notes.Trim();
        if (notes != null && notes.Length > 2000)
        {
            return Results.BadRequest(new { error = "notes must be at most 2000 characters" });
        }

        var hrZones = request.HrZones ?? BuildHrZonesSnapshot(
            await db.UserSettings.AsNoTracking().FirstOrDefaultAsync(),
            zoneService);

        if (hrZones.Zones.Count != 5 || hrZones.Zones.Any(z => z.MinBpm <= 0 || z.MaxBpm < z.MinBpm))
        {
            return Results.BadRequest(new { error = "hrZones must include five valid zones" });
        }

        var shoeSnapshots = await ResolveShoeSnapshotsAsync(db, request.Shoes, request.DefaultShoeId);
        if (shoeSnapshots.Error != null)
        {
            return Results.BadRequest(new { error = shoeSnapshots.Error });
        }

        var now = DateTime.UtcNow;
        var goal = await db.CoachGoals.FirstOrDefaultAsync();
        if (goal == null)
        {
            goal = new CoachGoal { CreatedAt = now };
            db.CoachGoals.Add(goal);
        }

        goal.Type = goalType;
        goal.Distance = distanceLabel;
        goal.DistanceM = distanceM;
        goal.RaceDate = raceDate;
        goal.TargetTimeS = targetTimeS;
        goal.Notes = notes;
        goal.UpdatedAt = now;

        var layer1Doc = new CoachLayer1Document(
            HrZones: hrZones,
            Paces: new CoachLayer1Paces(
                request.Paces.EasySecPerKm,
                request.Paces.TempoSecPerKm,
                request.Paces.ThresholdSecPerKm,
                request.Paces.IntervalSecPerKm),
            Shoes: shoeSnapshots.Shoes,
            DefaultShoeId: shoeSnapshots.DefaultShoeId,
            RaceStatus: new CoachLayer1RaceStatus(
                GoalType: goalType,
                Distance: distanceLabel,
                DistanceM: distanceM,
                RaceDate: raceDate?.ToString("yyyy-MM-dd"),
                TargetTimeS: targetTimeS,
                Notes: notes),
            CompletedAt: now);

        var layer = await db.CoachMemoryLayers.FirstOrDefaultAsync(l => l.Layer == Layer1Key);
        var json = CoachLayer1Json.Serialize(layer1Doc);
        if (layer == null)
        {
            db.CoachMemoryLayers.Add(new CoachMemoryLayer
            {
                Layer = Layer1Key,
                Document = json,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            layer.Document = json;
            layer.UpdatedAt = now;
        }

        var (seedError, _) = await CoachConstraintEndpoints.SeedFromRequestsAsync(
            db,
            request.Constraints,
            now);
        if (seedError != null)
        {
            return Results.BadRequest(new { error = seedError });
        }

        await db.SaveChangesAsync();
        return Results.Ok(await BuildOnboardingDocumentAsync(db));
    }

    private static async Task<bool> IsCoachAvailableAsync(TempoDbContext db, CoachConfig coachConfig)
    {
        if (!coachConfig.Enabled)
        {
            return false;
        }

        var prefs = await db.CoachPreferences.AsNoTracking().FirstOrDefaultAsync();
        return prefs?.OptedIn == true;
    }

    private static async Task<IResult?> EnsureTempoOnboardingAsync(ClaimsPrincipal principal, TempoDbContext db)
    {
        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Unauthorized();
        }

        var dbUser = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (dbUser == null)
        {
            return Results.Unauthorized();
        }

        if (!dbUser.OnboardingCompleted)
        {
            return Results.BadRequest(new
            {
                error = "Complete Tempo onboarding before Coach onboarding"
            });
        }

        return null;
    }

    private static async Task<CoachOnboardingDocument> BuildOnboardingDocumentAsync(TempoDbContext db)
    {
        var goal = await db.CoachGoals.AsNoTracking().FirstOrDefaultAsync();
        var layer = await db.CoachMemoryLayers.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Layer == Layer1Key);

        CoachLayer1Document? layer1 = null;
        if (layer != null)
        {
            try
            {
                layer1 = CoachLayer1Json.Deserialize(layer.Document);
            }
            catch (JsonException)
            {
                layer1 = null;
            }
        }

        var completed = goal != null && layer1 != null;
        return new CoachOnboardingDocument(
            Completed: completed,
            Goal: goal == null ? null : ToGoalDocument(goal),
            Layer1: layer1,
            Disclaimer:
                "Tempo AI Coach is not a doctor and does not provide medical advice. " +
                "It offers training suggestions only. Seek a qualified professional for pain, injury, or health concerns.");
    }

    internal static CoachGoalDocument ToGoalDocument(CoachGoal goal) =>
        new(
            Type: goal.Type,
            Distance: goal.Distance,
            DistanceM: goal.DistanceM,
            RaceDate: goal.RaceDate?.ToString("yyyy-MM-dd"),
            TargetTimeS: goal.TargetTimeS,
            Notes: goal.Notes);

    private static CoachLayer1HrZones BuildHrZonesSnapshot(
        UserSettings? settings,
        HeartRateZoneService zoneService)
    {
        if (settings == null)
        {
            const int defaultAge = 30;
            var defaultZones = zoneService.CalculateZonesFromAge(defaultAge);
            return new CoachLayer1HrZones(
                CalculationMethod: HeartRateCalculationMethod.AgeBased.ToString(),
                Age: defaultAge,
                RestingHeartRateBpm: null,
                MaxHeartRateBpm: 220 - defaultAge,
                Zones: defaultZones.Select((z, i) => new CoachLayer1HrZone(i + 1, z.MinBpm, z.MaxBpm)).ToList());
        }

        var zones = zoneService.GetZonesFromUserSettings(settings);
        return new CoachLayer1HrZones(
            CalculationMethod: settings.CalculationMethod.ToString(),
            Age: settings.Age,
            RestingHeartRateBpm: settings.RestingHeartRateBpm,
            MaxHeartRateBpm: settings.MaxHeartRateBpm,
            Zones: zones.Select((z, i) => new CoachLayer1HrZone(i + 1, z.MinBpm, z.MaxBpm)).ToList());
    }

    private static async Task<(IReadOnlyList<CoachLayer1Shoe> Shoes, Guid? DefaultShoeId, string? Error)>
        ResolveShoeSnapshotsAsync(
            TempoDbContext db,
            IReadOnlyList<CoachLayer1Shoe>? requested,
            Guid? defaultShoeId)
    {
        if (requested == null || requested.Count == 0)
        {
            var active = await db.Shoes.AsNoTracking()
                .Where(s => !s.IsRetired)
                .OrderBy(s => s.Brand)
                .ThenBy(s => s.Model)
                .Select(s => new CoachLayer1Shoe(s.Id, s.Brand, s.Model))
                .ToListAsync();

            var settings = await db.UserSettings.AsNoTracking().FirstOrDefaultAsync();
            var def = settings?.DefaultShoeId;
            if (def != null && active.All(s => s.Id != def))
            {
                def = null;
            }

            return (active, def, null);
        }

        // Persist athlete-confirmed snapshot fields as provided (no FK). Validate ids look like Guids.
        var shoes = new List<CoachLayer1Shoe>();
        foreach (var shoe in requested)
        {
            if (shoe.Id == Guid.Empty ||
                string.IsNullOrWhiteSpace(shoe.Brand) ||
                string.IsNullOrWhiteSpace(shoe.Model))
            {
                return ([], null, "Each shoe snapshot needs id, brand, and model");
            }

            shoes.Add(new CoachLayer1Shoe(shoe.Id, shoe.Brand.Trim(), shoe.Model.Trim()));
        }

        if (defaultShoeId != null && shoes.All(s => s.Id != defaultShoeId))
        {
            return ([], null, "defaultShoeId must match a shoe in the snapshot");
        }

        return (shoes, defaultShoeId, null);
    }
}

public record CoachGoalDocument(
    string Type,
    string Distance,
    double DistanceM,
    string? RaceDate,
    int? TargetTimeS,
    string? Notes);

public record CoachOnboardingDocument(
    bool Completed,
    CoachGoalDocument? Goal,
    CoachLayer1Document? Layer1,
    string Disclaimer);

public record CoachPrefillBestEffort(
    string Distance,
    double DistanceM,
    int TimeS,
    string WorkoutDate);

public record CoachOnboardingPrefillDocument(
    string UnitPreference,
    CoachLayer1HrZones HrZones,
    IReadOnlyList<CoachLayer1Shoe> Shoes,
    Guid? DefaultShoeId,
    IReadOnlyList<CoachPrefillBestEffort> BestEfforts,
    CoachLayer1Paces? SuggestedPaces,
    IReadOnlyList<string> GoalDistances,
    string Disclaimer);

public record CoachGoalRequest(
    string? Type,
    string? Distance,
    string? RaceDate,
    int? TargetTimeS,
    string? Notes);

public record CoachPacesRequest(
    int EasySecPerKm,
    int TempoSecPerKm,
    int ThresholdSecPerKm,
    int IntervalSecPerKm);

public record CompleteCoachOnboardingRequest(
    CoachGoalRequest? Goal,
    CoachPacesRequest? Paces,
    CoachLayer1HrZones? HrZones,
    IReadOnlyList<CoachLayer1Shoe>? Shoes,
    Guid? DefaultShoeId,
    IReadOnlyList<CoachConstraintRequest>? Constraints);
