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
                "Builds a Tempo-owned context packet for the Mon–Sun week, redacts name/email/GPS/route, " +
                "and persists a peelable CoachPrivacyPreflight (ConsentedAt null). " +
                "Fails closed with 400 if redaction cannot complete. Does not call Anthropic. " +
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
        CoachContextPacketAssembler assembler,
        [FromQuery] int? timezoneOffsetMinutes = null,
        [FromQuery] string? referenceDate = null)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        if (!TryResolveWeek(timezoneOffsetMinutes, referenceDate, out var week, out var weekError))
        {
            return Results.BadRequest(new { error = weekError });
        }

        var offset = timezoneOffsetMinutes ?? 0;
        var packet = await assembler.AssembleAsync(week, offset);

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync();
        var redactContext = new CoachRedactionContext(Username: user?.Username);

        if (!CoachPrivacyRedactor.TryRedact(
                packet,
                out var redactedJson,
                out var report,
                out var redactError,
                redactContext))
        {
            return Results.BadRequest(new { error = redactError ?? "Redaction failed" });
        }

        // Defense in depth: a "name" JSON property must never remain in the sendable packet.
        if (redactedJson.Contains("\"name\":", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { error = "Redaction failed: name fields must not appear in packet" });
        }

        var weekStartDate = DateOnly.FromDateTime(week.WeekStartLocal);
        var referenceLocal = DateOnly.FromDateTime(week.ReferenceLocalDate);
        var reportJson = JsonSerializer.Serialize(report, JsonOptions);
        var hash = Sha256Hex(redactedJson);
        var now = DateTime.UtcNow;

        var row = await db.CoachPrivacyPreflights.FirstOrDefaultAsync(p => p.WeekStartDate == weekStartDate);
        if (row == null)
        {
            row = new CoachPrivacyPreflight { WeekStartDate = weekStartDate };
            db.CoachPrivacyPreflights.Add(row);
        }

        row.TimezoneOffsetMinutes = offset;
        row.ReferenceDate = referenceLocal;
        row.RedactedPayloadJson = redactedJson;
        row.PrivacyReportJson = reportJson;
        row.PayloadHash = hash;
        row.AssembledAt = now;
        // Re-assemble clears prior consent — athlete must re-confirm this payload.
        row.ConsentedAt = null;

        await db.SaveChangesAsync();

        var weekEnd = week.WeekEndLocal.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Results.Ok(ToDocument(row, weekEnd));
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

        var weekEnd = row.WeekStartDate.AddDays(6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Results.Ok(ToDocument(row, weekEnd));
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

        var weekEnd = row.WeekStartDate.AddDays(6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Results.Ok(ToDocument(row, weekEnd));
    }

    private static CoachPrivacyPreflightDocument ToDocument(CoachPrivacyPreflight row, string weekEndDate)
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

        return new CoachPrivacyPreflightDocument(
            Id: row.Id,
            WeekStartDate: row.WeekStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            WeekEndDate: weekEndDate,
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

    private static bool TryResolveWeek(
        int? timezoneOffsetMinutes,
        string? referenceDate,
        out WeeklyRecapWeekMath.MondaySundayWeek week,
        out string? error)
    {
        week = null!;
        error = null;
        DateTime? referenceLocalDate = null;
        if (!string.IsNullOrWhiteSpace(referenceDate))
        {
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
        }

        week = WeeklyRecapWeekMath.ResolveMondaySundayWeek(
            DateTime.UtcNow,
            timezoneOffsetMinutes,
            referenceLocalDate);
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
    string ReferenceDate,
    int TimezoneOffsetMinutes,
    CoachPrivacyReport Report,
    object? RedactedPayload,
    string PayloadHash,
    bool Consented,
    DateTime AssembledAt,
    DateTime? ConsentedAt);
