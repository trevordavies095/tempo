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
            cancellationToken);
    }

    public async Task<(CoachPrivacyPreflight? Preflight, string? Error, int StatusCode)> AssembleAndPersistAsync(
        WeeklyRecapWeekMath.MondaySundayWeek checkInWeek,
        WeeklyRecapWeekMath.MondaySundayWeek planWeek,
        int timezoneOffsetMinutes,
        DateOnly referenceLocal,
        bool recordConsent,
        CancellationToken cancellationToken = default)
    {
        var packet = await _assembler.AssembleAsync(checkInWeek, planWeek, timezoneOffsetMinutes, cancellationToken);

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
            .FirstOrDefaultAsync(p => p.WeekStartDate == planStartDate, cancellationToken);
        if (row == null)
        {
            row = new CoachPrivacyPreflight { WeekStartDate = planStartDate };
            _db.CoachPrivacyPreflights.Add(row);
        }

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

    private static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
