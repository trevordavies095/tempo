using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Utils;

namespace Tempo.Api.Endpoints;

public static class CoachPrivacyEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/privacy/preflight", PostPreflight)
            .WithName("PostCoachPrivacyPreflight")
            .Produces<CoachPrivacyPreflightDocument>(200)
            .Produces(400)
            .Produces(404)
            .WithSummary("Assemble and redact coach context packet for privacy pre-flight")
            .WithDescription(
                "Builds a Tempo-owned context packet for the weekly ritual (check-in week N → plan week N+1) " +
                "or mid-week Adjust (plan week containing referenceDate). " +
                "Ritual assemble requires a persisted Coach check-in for week N. " +
                "Redacts name/email/GPS/route and persists a peelable CoachPrivacyPreflight (ConsentedAt null). " +
                "Fails closed with 400 if the check-in gate fails or redaction cannot complete. Does not call Anthropic. " +
                "404 when Coach is unavailable or the athlete has not opted in.");

        group.MapGet("/privacy/preflight/{id:guid}", GetPreflight)
            .WithName("GetCoachPrivacyPreflight")
            .Produces<CoachPrivacyPreflightDocument>(200)
            .Produces(404)
            .WithSummary("Get a privacy pre-flight by id")
            .WithDescription(
                "Returns the stored redacted payload, privacy report, and consent status. " +
                "404 when missing or Coach unavailable.");

        group.MapPost("/privacy/preflight/{id:guid}/consent", PostConsent)
            .WithName("PostCoachPrivacyPreflightConsent")
            .Produces<CoachPrivacyPreflightDocument>(200)
            .Produces(404)
            .WithSummary("Consent to send the redacted packet")
            .WithDescription(
                "Sets ConsentedAt so a later generate job may use this pre-flight. " +
                "Does not call Anthropic. Idempotent if already consented.");
    }

    private static async Task<IResult> PostPreflight(
        TempoDbContext db,
        CoachConfig coachConfig,
        CoachPrivacyPreflightService preflightService,
        [FromQuery] int? timezoneOffsetMinutes = null,
        [FromQuery] string? referenceDate = null)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        if (!TryResolveContainingWeek(timezoneOffsetMinutes, referenceDate, out var containingWeek, out var weekError))
        {
            return Results.BadRequest(new { error = weekError });
        }

        var offset = timezoneOffsetMinutes ?? 0;
        var containingMonday = DateOnly.FromDateTime(containingWeek.WeekStartLocal);

        var startedAdjust = await db.CoachAdjustments.AsNoTracking()
            .AnyAsync(
                a => a.WeekStartDate == containingMonday && a.Status == CoachAdjustmentStatuses.Started);

        CoachPrivacyPreflight? row;
        string? assembleError;
        WeeklyRecapWeekMath.MondaySundayWeek checkInWeek;
        WeeklyRecapWeekMath.MondaySundayWeek planWeek;

        if (startedAdjust)
        {
            // Adjust: referenceDate identifies the plan week; no check-in gate.
            planWeek = containingWeek;
            checkInWeek = WeeklyRecapWeekMath.ResolveCheckInWeekFromPlan(planWeek, offset);
            var referenceLocal = DateOnly.FromDateTime(planWeek.ReferenceLocalDate);
            (row, assembleError, _) = await preflightService.AssembleAndPersistAsync(
                checkInWeek,
                planWeek,
                offset,
                referenceLocal,
                recordConsent: false);
        }
        else
        {
            // Ritual: referenceDate (or issue-16 default) identifies check-in week N; plan = N+7.
            if (!TryResolveCheckInWeek(timezoneOffsetMinutes, referenceDate, out checkInWeek, out weekError))
            {
                return Results.BadRequest(new { error = weekError });
            }

            planWeek = WeeklyRecapWeekMath.ResolvePlanWeekFromCheckIn(checkInWeek, offset);
            (row, assembleError, _) = await preflightService.AssembleRitualAsync(
                checkInWeek,
                offset,
                recordConsent: false);
        }

        if (row == null)
        {
            return Results.BadRequest(new { error = assembleError ?? "Redaction failed" });
        }

        var checkInStartDate = DateOnly.FromDateTime(checkInWeek.WeekStartLocal);
        var planStartDate = DateOnly.FromDateTime(planWeek.WeekStartLocal);

        return Results.Ok(ToDocument(
            row,
            checkInStartDate,
            DateOnly.FromDateTime(checkInWeek.WeekEndLocal.Date),
            planStartDate,
            DateOnly.FromDateTime(planWeek.WeekEndLocal.Date)));
    }

    private static async Task<IResult> GetPreflight(
        Guid id,
        TempoDbContext db,
        CoachConfig coachConfig)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var row = await db.CoachPrivacyPreflights.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (row == null)
        {
            return Results.NotFound();
        }

        return Results.Ok(ToDocumentFromRow(row));
    }

    private static async Task<IResult> PostConsent(
        Guid id,
        TempoDbContext db,
        CoachConfig coachConfig)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var row = await db.CoachPrivacyPreflights.FirstOrDefaultAsync(p => p.Id == id);
        if (row == null)
        {
            return Results.NotFound();
        }

        row.ConsentedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Results.Ok(ToDocumentFromRow(row));
    }

    private static CoachPrivacyPreflightDocument ToDocumentFromRow(CoachPrivacyPreflight row)
    {
        var planStart = row.WeekStartDate;
        var planEnd = planStart.AddDays(6);
        var checkInStart = planStart.AddDays(-7);
        var checkInEnd = checkInStart.AddDays(6);
        return ToDocument(row, checkInStart, checkInEnd, planStart, planEnd);
    }

    private static CoachPrivacyPreflightDocument ToDocument(
        CoachPrivacyPreflight row,
        DateOnly checkInStart,
        DateOnly checkInEnd,
        DateOnly planStart,
        DateOnly planEnd)
    {
        var report = JsonSerializer.Deserialize<CoachPrivacyReport>(row.PrivacyReportJson, JsonOptions)
                     ?? new CoachPrivacyReport([], [], [], []);
        object? payload;
        try
        {
            payload = JsonSerializer.Deserialize<object>(row.RedactedPayloadJson, JsonOptions);
        }
        catch
        {
            payload = null;
        }

        var planStartStr = planStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var planEndStr = planEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var checkInStartStr = checkInStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var checkInEndStr = checkInEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return new CoachPrivacyPreflightDocument(
            Id: row.Id,
            WeekStartDate: planStartStr,
            WeekEndDate: planEndStr,
            CheckInWeekStartDate: checkInStartStr,
            CheckInWeekEndDate: checkInEndStr,
            PlanWeekStartDate: planStartStr,
            PlanWeekEndDate: planEndStr,
            ReferenceDate: row.ReferenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimezoneOffsetMinutes: row.TimezoneOffsetMinutes,
            Report: report,
            RedactedPayload: payload,
            PayloadHash: row.PayloadHash,
            Consented: row.ConsentedAt != null,
            AssembledAt: row.AssembledAt,
            ConsentedAt: row.ConsentedAt);
    }

    private static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Week containing referenceDate (or local today) — used to detect Adjust on that Monday.
    /// </summary>
    private static bool TryResolveContainingWeek(
        int? timezoneOffsetMinutes,
        string? referenceDate,
        out WeeklyRecapWeekMath.MondaySundayWeek week,
        out string? error)
    {
        week = null!;
        error = null;
        if (!TryParseReferenceDate(referenceDate, out var referenceLocalDate, out error))
        {
            return false;
        }

        week = WeeklyRecapWeekMath.ResolveMondaySundayWeek(
            DateTime.UtcNow,
            timezoneOffsetMinutes,
            referenceLocalDate);
        return true;
    }

    /// <summary>
    /// Check-in week for the ritual: explicit referenceDate, or issue-16 default when omitted.
    /// </summary>
    private static bool TryResolveCheckInWeek(
        int? timezoneOffsetMinutes,
        string? referenceDate,
        out WeeklyRecapWeekMath.MondaySundayWeek week,
        out string? error)
    {
        week = null!;
        error = null;
        if (!TryParseReferenceDate(referenceDate, out var referenceLocalDate, out error))
        {
            return false;
        }

        week = referenceLocalDate.HasValue
            ? WeeklyRecapWeekMath.ResolveMondaySundayWeek(
                DateTime.UtcNow,
                timezoneOffsetMinutes,
                referenceLocalDate)
            : WeeklyRecapWeekMath.ResolveDefaultCheckInWeek(DateTime.UtcNow, timezoneOffsetMinutes);
        return true;
    }

    private static bool TryParseReferenceDate(
        string? referenceDate,
        out DateTime? referenceLocalDate,
        out string? error)
    {
        referenceLocalDate = null;
        error = null;
        if (string.IsNullOrWhiteSpace(referenceDate))
        {
            return true;
        }

        if (!DateTime.TryParse(
                referenceDate,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            error = "Invalid referenceDate; use yyyy-MM-dd.";
            return false;
        }

        referenceLocalDate = parsed.Date;
        return true;
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
}

public sealed record CoachPrivacyPreflightDocument(
    Guid Id,
    string WeekStartDate,
    string WeekEndDate,
    string CheckInWeekStartDate,
    string CheckInWeekEndDate,
    string PlanWeekStartDate,
    string PlanWeekEndDate,
    string ReferenceDate,
    int TimezoneOffsetMinutes,
    CoachPrivacyReport Report,
    object? RedactedPayload,
    string PayloadHash,
    bool Consented,
    DateTime AssembledAt,
    DateTime? ConsentedAt);
