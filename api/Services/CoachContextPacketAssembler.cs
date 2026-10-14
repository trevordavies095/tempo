using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Utils;

namespace Tempo.Api.Services;

/// <summary>
/// Assembles a Tempo-owned coach context packet for privacy pre-flight / provider send.
/// Does not load routes, raw workout files, or athlete username.
/// </summary>
public class CoachContextPacketAssembler
{
    public const string Layer1Key = "L1";

    private readonly TempoDbContext _db;
    private readonly HeartRateZoneService _zoneService;

    public CoachContextPacketAssembler(TempoDbContext db, HeartRateZoneService zoneService)
    {
        _db = db;
        _zoneService = zoneService;
    }

    public async Task<CoachContextPacket> AssembleAsync(
        WeeklyRecapWeekMath.MondaySundayWeek week,
        int timezoneOffsetMinutes,
        CancellationToken cancellationToken = default)
    {
        var weekStartDate = DateOnly.FromDateTime(week.WeekStartLocal);
        var weekStart = weekStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var weekEnd = week.WeekEndLocal.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var referenceDate = week.ReferenceLocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var settings = await _db.UserSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var unitPreference = string.IsNullOrWhiteSpace(settings?.UnitPreference)
            ? "metric"
            : settings!.UnitPreference!;

        var goal = await _db.CoachGoals.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        CoachPacketGoal? packetGoal = null;
        if (goal != null)
        {
            packetGoal = new CoachPacketGoal(
                goal.Type,
                goal.Distance,
                goal.DistanceM,
                goal.RaceDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                goal.TargetTimeS,
                goal.Notes);
        }

        var constraints = await _db.CoachConstraints.AsNoTracking()
            .Where(c => !c.IsRetired)
            .OrderBy(c => c.Kind)
            .ThenBy(c => c.StartDate)
            .ToListAsync(cancellationToken);

        var packetConstraints = constraints.Select(c => new CoachPacketConstraint(
            c.Kind,
            c.StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            c.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            c.Distance,
            c.DistanceM,
            c.Intent,
            c.Preset,
            c.Weekdays,
            c.Note)).ToList();

        var layer1 = await BuildLayer1Async(settings, cancellationToken);

        var checkInRow = await _db.CoachCheckIns.AsNoTracking()
            .FirstOrDefaultAsync(c => c.WeekStartDate == weekStartDate, cancellationToken);

        var range = week.FullWeekRange;
        var workouts = await _db.Workouts.AsNoTracking()
            .Where(w => w.StartedAt >= range.StartUtc && w.StartedAt <= range.EndUtc)
            .OrderBy(w => w.StartedAt)
            .Select(w => new
            {
                w.Id,
                w.StartedAt,
                w.DistanceM,
                w.DurationS,
                w.AvgHeartRateBpm,
                w.RunType,
                w.Notes
            })
            .ToListAsync(cancellationToken);

        var noteOverlays = CoachCheckInRunNotesJson.Deserialize(checkInRow?.RunNotesJson)
            .GroupBy(n => n.WorkoutId)
            .ToDictionary(g => g.Key, g => g.Last().Notes);

        var runs = workouts.Select(w =>
        {
            noteOverlays.TryGetValue(w.Id, out var overlay);
            var hasOverlay = noteOverlays.ContainsKey(w.Id);
            return new CoachPacketCheckInRun(
                WorkoutId: w.Id,
                StartedAt: w.StartedAt,
                DistanceM: w.DistanceM,
                DurationS: w.DurationS,
                AvgHeartRateBpm: w.AvgHeartRateBpm,
                RunType: w.RunType,
                Notes: hasOverlay ? overlay : w.Notes);
        }).ToList();

        var checkIn = new CoachPacketCheckIn(
            WeekStartDate: weekStart,
            WeekEndDate: weekEnd,
            OverallFeel: checkInRow?.OverallFeel,
            Niggles: checkInRow?.Niggles,
            LifeConstraints: checkInRow?.LifeConstraints,
            Runs: runs);

        var adjustment = await BuildAdjustmentAsync(weekStartDate, cancellationToken);

        return new CoachContextPacket(
            Meta: new CoachPacketMeta(
                WeekStartDate: weekStart,
                WeekEndDate: weekEnd,
                ReferenceDate: referenceDate,
                TimezoneOffsetMinutes: timezoneOffsetMinutes,
                UnitPreference: unitPreference,
                AssembledAtUtc: DateTime.UtcNow),
            Goal: packetGoal,
            Constraints: packetConstraints,
            Layer1: layer1,
            Layer2: new Dictionary<string, object>(),
            Layer3: new Dictionary<string, object>(),
            CheckIn: checkIn,
            Adjustment: adjustment);
    }

    private async Task<CoachPacketAdjustment?> BuildAdjustmentAsync(
        DateOnly weekStartDate,
        CancellationToken cancellationToken)
    {
        var started = await _db.CoachAdjustments.AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.WeekStartDate == weekStartDate && a.Status == CoachAdjustmentStatuses.Started,
                cancellationToken);
        if (started == null)
        {
            return null;
        }

        var source = await _db.CoachWeeks.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == started.SourceCommittedWeekId, cancellationToken);
        if (source == null)
        {
            return null;
        }

        var schemaError = CoachWeeklyCommitSchema.TryParseAndValidate(
            source.CommitJson,
            weekStartDate,
            out var commit);
        if (schemaError != null || commit == null)
        {
            return null;
        }

        var lockedThrough = started.LockedThroughDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var lockedDays = new List<object>();
        var remainingDates = new List<string>();
        foreach (var day in commit.Days.OrderBy(d => d.Date, StringComparer.Ordinal))
        {
            if (!DateOnly.TryParse(day.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                continue;
            }

            if (date <= started.LockedThroughDate)
            {
                lockedDays.Add(JsonSerializer.Deserialize<object>(
                    JsonSerializer.Serialize(day, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    }))!);
            }
            else
            {
                remainingDates.Add(day.Date);
            }
        }

        return new CoachPacketAdjustment(
            Id: started.Id,
            Reason: started.Reason,
            Category: started.Category,
            LockedThroughDate: lockedThrough,
            LockedDays: lockedDays,
            RemainingDates: remainingDates);
    }

    private async Task<CoachPacketLayer1> BuildLayer1Async(
        UserSettings? settings,
        CancellationToken cancellationToken)
    {
        var liveHr = BuildHrZonesSnapshot(settings);
        var liveShoes = await _db.Shoes.AsNoTracking()
            .Where(s => !s.IsRetired)
            .OrderBy(s => s.Brand)
            .ThenBy(s => s.Model)
            .Select(s => new CoachLayer1Shoe(s.Id, s.Brand, s.Model))
            .ToListAsync(cancellationToken);

        var defaultShoeId = settings?.DefaultShoeId;
        if (defaultShoeId != null && liveShoes.All(s => s.Id != defaultShoeId))
        {
            defaultShoeId = null;
        }

        var layerRow = await _db.CoachMemoryLayers.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Layer == Layer1Key, cancellationToken);

        CoachLayer1Document? persisted = null;
        if (layerRow != null)
        {
            persisted = CoachLayer1Json.Deserialize(layerRow.Document);
        }

        var paces = persisted?.Paces ?? new CoachLayer1Paces(0, 0, 0, 0);
        var raceStatus = persisted?.RaceStatus ?? new CoachLayer1RaceStatus(
            GoalType: "none",
            Distance: "",
            DistanceM: 0,
            RaceDate: null,
            TargetTimeS: null,
            Notes: null);

        return new CoachPacketLayer1(
            HrZones: liveHr,
            Paces: paces,
            Shoes: liveShoes,
            DefaultShoeId: defaultShoeId ?? persisted?.DefaultShoeId,
            RaceStatus: raceStatus);
    }

    private CoachLayer1HrZones BuildHrZonesSnapshot(UserSettings? settings)
    {
        if (settings == null)
        {
            const int defaultAge = 30;
            var defaultZones = _zoneService.CalculateZonesFromAge(defaultAge);
            return new CoachLayer1HrZones(
                CalculationMethod: HeartRateCalculationMethod.AgeBased.ToString(),
                Age: defaultAge,
                RestingHeartRateBpm: null,
                MaxHeartRateBpm: 220 - defaultAge,
                Zones: defaultZones.Select((z, i) => new CoachLayer1HrZone(i + 1, z.MinBpm, z.MaxBpm)).ToList());
        }

        var zones = _zoneService.GetZonesFromUserSettings(settings);
        return new CoachLayer1HrZones(
            CalculationMethod: settings.CalculationMethod.ToString(),
            Age: settings.Age,
            RestingHeartRateBpm: settings.RestingHeartRateBpm,
            MaxHeartRateBpm: settings.MaxHeartRateBpm,
            Zones: zones.Select((z, i) => new CoachLayer1HrZone(i + 1, z.MinBpm, z.MaxBpm)).ToList());
    }
}

public sealed record CoachContextPacket(
    CoachPacketMeta Meta,
    CoachPacketGoal? Goal,
    IReadOnlyList<CoachPacketConstraint> Constraints,
    CoachPacketLayer1 Layer1,
    Dictionary<string, object> Layer2,
    Dictionary<string, object> Layer3,
    CoachPacketCheckIn CheckIn,
    CoachPacketAdjustment? Adjustment = null);

public sealed record CoachPacketAdjustment(
    Guid Id,
    string Reason,
    string? Category,
    string LockedThroughDate,
    IReadOnlyList<object> LockedDays,
    IReadOnlyList<string> RemainingDates);

public sealed record CoachPacketMeta(
    string WeekStartDate,
    string WeekEndDate,
    string ReferenceDate,
    int TimezoneOffsetMinutes,
    string UnitPreference,
    DateTime AssembledAtUtc);

public sealed record CoachPacketGoal(
    string Type,
    string Distance,
    double DistanceM,
    string? RaceDate,
    int? TargetTimeS,
    string? Notes);

public sealed record CoachPacketConstraint(
    string Kind,
    string? StartDate,
    string? EndDate,
    string? Distance,
    double? DistanceM,
    string? Intent,
    string? Preset,
    IReadOnlyList<int>? Weekdays,
    string? Note);

public sealed record CoachPacketLayer1(
    CoachLayer1HrZones HrZones,
    CoachLayer1Paces Paces,
    IReadOnlyList<CoachLayer1Shoe> Shoes,
    Guid? DefaultShoeId,
    CoachLayer1RaceStatus RaceStatus);

public sealed record CoachPacketCheckIn(
    string WeekStartDate,
    string WeekEndDate,
    string? OverallFeel,
    string? Niggles,
    string? LifeConstraints,
    IReadOnlyList<CoachPacketCheckInRun> Runs);

public sealed record CoachPacketCheckInRun(
    Guid WorkoutId,
    DateTime StartedAt,
    double DistanceM,
    int DurationS,
    byte? AvgHeartRateBpm,
    string? RunType,
    string? Notes);
