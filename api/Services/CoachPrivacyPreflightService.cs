using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Utils;

namespace Tempo.Api.Services;

/// <summary>
/// Assemble + redact + persist privacy preflight rows (shared by preflight POST and ritual Send).
/// </summary>
public sealed class CoachPrivacyPreflightService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly TempoDbContext _db;
    private readonly CoachContextPacketAssembler _assembler;

    public CoachPrivacyPreflightService(TempoDbContext db, CoachContextPacketAssembler assembler)
    {
        _db = db;
        _assembler = assembler;
    }

    /// <summary>
    /// Ritual path: check-in week N → plan week N+1. Optionally records consent in the same save.
    /// </summary>
    public async Task<(CoachPrivacyPreflight? Preflight, string? Error, int StatusCode)> AssembleRitualAsync(
        WeeklyRecapWeekMath.MondaySundayWeek checkInWeek,
        int timezoneOffsetMinutes,
        bool recordConsent,
        CancellationToken cancellationToken = default)
    {
        var planWeek = WeeklyRecapWeekMath.ResolvePlanWeekFromCheckIn(checkInWeek, timezoneOffsetMinutes);
        var checkInMonday = DateOnly.FromDateTime(checkInWeek.WeekStartLocal);
        var planMonday = DateOnly.FromDateTime(planWeek.WeekStartLocal);

        var hasCheckIn = await _db.CoachCheckIns.AsNoTracking()
            .AnyAsync(c => c.WeekStartDate == checkInMonday, cancellationToken);
        if (!hasCheckIn)
        {
            return (
                null,
                $"Save a Coach check-in for {checkInMonday:yyyy-MM-dd} before generating the plan for {planMonday:yyyy-MM-dd}.",
                400);
        }

        return await AssembleAndPersistAsync(
            checkInWeek,
            planWeek,
            timezoneOffsetMinutes,
            referenceLocal: DateOnly.FromDateTime(checkInWeek.ReferenceLocalDate),
            recordConsent,
            athleteFeedback: null,
            cancellationToken);
    }

    public async Task<(CoachPrivacyPreflight? Preflight, string? Error, int StatusCode)> AssembleAndPersistAsync(
        WeeklyRecapWeekMath.MondaySundayWeek checkInWeek,
        WeeklyRecapWeekMath.MondaySundayWeek planWeek,
        int timezoneOffsetMinutes,
        DateOnly referenceLocal,
        bool recordConsent,
        CoachPacketAthleteFeedback? athleteFeedback = null,
        CancellationToken cancellationToken = default)
    {
        var packet = await _assembler.AssembleAsync(checkInWeek, planWeek, timezoneOffsetMinutes, cancellationToken);
        if (athleteFeedback != null)
        {
            packet = packet with { AthleteFeedback = athleteFeedback };
        }

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var redactContext = new CoachRedactionContext(Username: user?.Username);

        if (!CoachPrivacyRedactor.TryRedact(
                packet,
                out var redactedJson,
                out var report,
                out var redactError,
                redactContext))
        {
            return (null, redactError ?? "Redaction failed", 400);
        }

        if (redactedJson.Contains("\"name\":", StringComparison.OrdinalIgnoreCase))
        {
            return (null, "Redaction failed: name fields must not appear in packet", 400);
        }

        var planStartDate = DateOnly.FromDateTime(planWeek.WeekStartLocal);
        var reportJson = JsonSerializer.Serialize(report, JsonOptions);
        var hash = Sha256Hex(redactedJson);
        var now = DateTime.UtcNow;

        var row = await _db.CoachPrivacyPreflights
            .FirstOrDefaultAsync(
                p => p.WeekStartDate == planStartDate
                     && p.Kind == CoachPrivacyPreflightKinds.Weekly,
                cancellationToken);
        if (row == null)
        {
            row = new CoachPrivacyPreflight
            {
                WeekStartDate = planStartDate,
                Kind = CoachPrivacyPreflightKinds.Weekly
            };
            _db.CoachPrivacyPreflights.Add(row);
        }

        row.Kind = CoachPrivacyPreflightKinds.Weekly;
        row.ExchangeId = null;
        row.TimezoneOffsetMinutes = timezoneOffsetMinutes;
        row.ReferenceDate = referenceLocal;
        row.RedactedPayloadJson = redactedJson;
        row.PrivacyReportJson = reportJson;
        row.PayloadHash = hash;
        row.AssembledAt = now;
        row.ConsentedAt = recordConsent ? now : null;

        await _db.SaveChangesAsync(cancellationToken);
        return (row, null, 200);
    }

    /// <summary>
    /// Draft Ask-for-changes: re-assemble plan week with athleteFeedback, consent, ready for enqueue.
    /// </summary>
    public async Task<(CoachPrivacyPreflight? Preflight, string? Error, int StatusCode)> AssembleDraftAskForChangesAsync(
        DateOnly planMonday,
        string note,
        string previousDraftCommitJson,
        int timezoneOffsetMinutes,
        CancellationToken cancellationToken = default)
    {
        var planWeek = WeeklyRecapWeekMath.ResolveMondaySundayWeek(
            utcNow: DateTime.SpecifyKind(planMonday.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
            timezoneOffsetMinutes,
            referenceLocalDate: planMonday.ToDateTime(TimeOnly.MinValue));
        var checkInWeek = WeeklyRecapWeekMath.ResolveCheckInWeekFromPlan(planWeek, timezoneOffsetMinutes);
        var checkInMonday = DateOnly.FromDateTime(checkInWeek.WeekStartLocal);

        var hasCheckIn = await _db.CoachCheckIns.AsNoTracking()
            .AnyAsync(c => c.WeekStartDate == checkInMonday, cancellationToken);
        if (!hasCheckIn)
        {
            return (
                null,
                $"Save a Coach check-in for {checkInMonday:yyyy-MM-dd} before generating the plan for {planMonday:yyyy-MM-dd}.",
                400);
        }

        if (!TryBuildAthleteFeedback(note, previousDraftCommitJson, out var feedback, out var feedbackError))
        {
            return (null, feedbackError, 400);
        }

        return await AssembleAndPersistAsync(
            checkInWeek,
            planWeek,
            timezoneOffsetMinutes,
            referenceLocal: DateOnly.FromDateTime(checkInWeek.ReferenceLocalDate),
            recordConsent: true,
            athleteFeedback: feedback,
            cancellationToken);
    }

    /// <summary>
    /// Exchange path: assemble + redact for plan week of reference local date.
    /// Creates a new exchange-kind preflight (never upserts weekly).
    /// </summary>
    public async Task<(CoachPrivacyPreflight? Preflight, string? Error, int StatusCode)> AssembleExchangeAsync(
        DateOnly planMonday,
        DateOnly referenceLocal,
        int timezoneOffsetMinutes,
        string athleteText,
        Guid? exchangeId,
        bool recordConsent,
        Guid? workoutId = null,
        Guid? excludeExchangeId = null,
        CancellationToken cancellationToken = default)
    {
        var planWeek = WeeklyRecapWeekMath.ResolveMondaySundayWeek(
            utcNow: DateTime.SpecifyKind(referenceLocal.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
            timezoneOffsetMinutes,
            referenceLocalDate: referenceLocal.ToDateTime(TimeOnly.MinValue));

        // Ensure week identity matches plan Monday even if reference drifts.
        if (DateOnly.FromDateTime(planWeek.WeekStartLocal) != planMonday)
        {
            planWeek = WeeklyRecapWeekMath.ResolveMondaySundayWeek(
                utcNow: DateTime.SpecifyKind(planMonday.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
                timezoneOffsetMinutes,
                referenceLocalDate: planMonday.ToDateTime(TimeOnly.MinValue));
        }

        var (packet, assembleError) = await _assembler.AssembleExchangeAsync(
            planWeek,
            timezoneOffsetMinutes,
            athleteText,
            workoutId,
            excludeExchangeId,
            cancellationToken);
        if (packet == null)
        {
            return (null, assembleError ?? "Failed to assemble exchange packet", 400);
        }

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var redactContext = new CoachRedactionContext(Username: user?.Username);

        if (!CoachPrivacyRedactor.TryRedact(
                packet,
                out var redactedJson,
                out var report,
                out var redactError,
                redactContext))
        {
            return (null, redactError ?? "Redaction failed", 400);
        }

        if (redactedJson.Contains("\"name\":", StringComparison.OrdinalIgnoreCase))
        {
            return (null, "Redaction failed: name fields must not appear in packet", 400);
        }

        var now = DateTime.UtcNow;
        var row = new CoachPrivacyPreflight
        {
            Kind = CoachPrivacyPreflightKinds.Exchange,
            WeekStartDate = planMonday,
            ExchangeId = exchangeId,
            TimezoneOffsetMinutes = timezoneOffsetMinutes,
            ReferenceDate = referenceLocal,
            RedactedPayloadJson = redactedJson,
            PrivacyReportJson = JsonSerializer.Serialize(report, JsonOptions),
            PayloadHash = Sha256Hex(redactedJson),
            AssembledAt = now,
            ConsentedAt = recordConsent ? now : null
        };
        _db.CoachPrivacyPreflights.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        return (row, null, 200);
    }

    public static bool TryBuildAthleteFeedback(
        string note,
        string commitJson,
        out CoachPacketAthleteFeedback? feedback,
        out string? error)
    {
        feedback = null;
        error = null;

        if (string.IsNullOrWhiteSpace(note))
        {
            error = "note is required";
            return false;
        }

        CoachWeeklyCommit? commit;
        try
        {
            commit = JsonSerializer.Deserialize<CoachWeeklyCommit>(commitJson, JsonOptions);
        }
        catch (JsonException)
        {
            error = "Draft commit could not be parsed";
            return false;
        }

        if (commit == null)
        {
            error = "Draft commit is empty";
            return false;
        }

        var days = commit.Days.Select(d => new CoachPacketPreviousDraftDay(
            Date: d.Date,
            DayType: d.DayType,
            DistanceM: d.Run?.DistanceM,
            Structure: d.Run?.Structure)).ToList();

        feedback = new CoachPacketAthleteFeedback(
            Note: note.Trim(),
            PreviousDraft: new CoachPacketPreviousDraft(days, commit.CoachesNotes ?? string.Empty));
        return true;
    }

    private static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
