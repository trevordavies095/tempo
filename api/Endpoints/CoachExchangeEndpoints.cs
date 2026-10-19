using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Utils;

namespace Tempo.Api.Endpoints;

public static class CoachExchangeEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/exchanges/preflight", PostPreflight)
            .WithName("PostCoachExchangePreflight")
            .Accepts<PostCoachExchangeRequest>("application/json")
            .Produces<CoachExchangePreflightDocument>(200)
            .Produces(400)
            .Produces(404)
            .WithSummary("Assemble an exchange privacy preflight for Inspect (no consent)")
            .WithDescription(
                "Assembles and redacts an exchange packet without recording consent or enqueueing. " +
                "Send records consent. Text may be empty for Inspect while composing.");

        group.MapPost("/exchanges", PostSend)
            .WithName("PostCoachExchange")
            .Accepts<PostCoachExchangeRequest>("application/json")
            .Produces<CoachExchangeSendDocument>(202)
            .Produces(400)
            .Produces(404)
            .Produces(409)
            .WithSummary("Send a message exchange to the coach (optional attached workout)")
            .WithDescription(
                "Consent is the click. Requires non-empty athleteText when no workout is attached; " +
                "text is optional when workoutId is set (Tempo supplies run context). " +
                "Creates a peelable CoachExchange, consented exchange preflight, and exchange job. " +
                "409 when another exchange job is active. Ritual jobs may run concurrently.");

        group.MapGet("/exchanges", ListExchanges)
            .WithName("ListCoachExchanges")
            .Produces<IReadOnlyList<CoachExchangeDocument>>(200)
            .Produces(400)
            .Produces(404)
            .WithSummary("List exchanges for a plan week (newest first)");

        group.MapGet("/exchanges/{id:guid}", GetExchange)
            .WithName("GetCoachExchange")
            .Produces<CoachExchangeDocument>(200)
            .Produces(404)
            .WithSummary("Get one exchange including reply when ready");

        group.MapGet("/exchanges/{id:guid}/privacy-receipt", GetPrivacyReceipt)
            .WithName("GetCoachExchangePrivacyReceipt")
            .Produces<CoachExchangePrivacyReceiptDocument>(200)
            .Produces(404)
            .WithSummary("Get the privacy receipt for an exchange");

        group.MapPost("/exchanges/{id:guid}/retry", PostRetry)
            .WithName("PostCoachExchangeRetry")
            .Accepts<PostCoachExchangeRetryRequest>("application/json")
            .Produces<CoachExchangeSendDocument>(202)
            .Produces(400)
            .Produces(404)
            .Produces(409)
            .WithSummary("Retry a failed exchange preserving athlete text");
    }

    private static async Task<IResult> PostPreflight(
        [FromBody] PostCoachExchangeRequest? request,
        TempoDbContext db,
        CoachConfig coachConfig,
        CoachPrivacyPreflightService preflightService)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var offset = request?.TimezoneOffsetMinutes ?? 0;
        var text = request?.AthleteText?.Trim() ?? string.Empty;
        var (planMonday, referenceLocal, workoutId, weekError) = await ResolveWeekAsync(
            db,
            request?.ReferenceDate,
            offset,
            request?.WorkoutId);
        if (weekError != null)
        {
            return Results.BadRequest(new { error = weekError });
        }

        var (preflight, assembleError, assembleStatus) = await preflightService.AssembleExchangeAsync(
            planMonday,
            referenceLocal,
            offset,
            text,
            exchangeId: null,
            recordConsent: false,
            workoutId: workoutId);
        if (preflight == null)
        {
            return assembleStatus switch
            {
                400 => Results.BadRequest(new { error = assembleError }),
                _ => Results.BadRequest(new { error = assembleError ?? "Failed to assemble privacy packet" })
            };
        }

        return Results.Ok(ToPreflightDocument(preflight));
    }

    private static async Task<IResult> PostSend(
        [FromBody] PostCoachExchangeRequest? request,
        TempoDbContext db,
        CoachConfig coachConfig,
        CoachPrivacyPreflightService preflightService,
        CoachJobService jobService)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var text = request?.AthleteText?.Trim() ?? string.Empty;
        var workoutId = request?.WorkoutId;
        if (text.Length == 0 && !workoutId.HasValue)
        {
            return Results.BadRequest(new { error = "athleteText is required when no workout is attached" });
        }

        if (text.Length > 4000)
        {
            return Results.BadRequest(new { error = "athleteText must be at most 4000 characters" });
        }

        var offset = request?.TimezoneOffsetMinutes ?? 0;
        var (planMonday, referenceLocal, resolvedWorkoutId, weekError) = await ResolveWeekAsync(
            db,
            request?.ReferenceDate,
            offset,
            workoutId);
        if (weekError != null)
        {
            return Results.BadRequest(new { error = weekError });
        }

        var now = DateTime.UtcNow;
        var exchange = new CoachExchange
        {
            WeekStartDate = planMonday,
            WorkoutId = resolvedWorkoutId,
            AthleteText = text,
            Status = CoachExchangeStatuses.Queued,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.CoachExchanges.Add(exchange);
        await db.SaveChangesAsync();

        var (preflight, assembleError, assembleStatus) = await preflightService.AssembleExchangeAsync(
            planMonday,
            referenceLocal,
            offset,
            text,
            exchangeId: exchange.Id,
            recordConsent: true,
            workoutId: resolvedWorkoutId,
            excludeExchangeId: exchange.Id);
        if (preflight == null)
        {
            db.CoachExchanges.Remove(exchange);
            await db.SaveChangesAsync();
            return assembleStatus switch
            {
                400 => Results.BadRequest(new { error = assembleError }),
                _ => Results.BadRequest(new { error = assembleError ?? "Failed to assemble privacy packet" })
            };
        }

        exchange.PreflightId = preflight.Id;
        await db.SaveChangesAsync();

        var (job, jobError, jobStatus) = await jobService.StartExchangeAsync(preflight.Id, exchange.Id);
        if (job == null)
        {
            exchange.Status = CoachExchangeStatuses.Failed;
            exchange.ErrorCode = jobStatus == 409 ? "conflict" : "validation";
            exchange.ErrorMessage = jobError;
            exchange.CompletedAt = DateTime.UtcNow;
            exchange.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return jobStatus switch
            {
                404 => Results.NotFound(new { error = jobError }),
                409 => Results.Conflict(new { error = jobError }),
                _ => Results.BadRequest(new { error = jobError })
            };
        }

        // Reload exchange after job service updates.
        exchange = await db.CoachExchanges.AsNoTracking().FirstAsync(e => e.Id == exchange.Id);
        return Results.Json(
            new CoachExchangeSendDocument(ToJobDocument(job), ToDocument(exchange)),
            statusCode: 202);
    }

    private static async Task<IResult> ListExchanges(
        TempoDbContext db,
        CoachConfig coachConfig,
        [FromQuery] string? weekStartDate,
        [FromQuery] int? timezoneOffsetMinutes,
        [FromQuery] Guid? workoutId)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        IQueryable<CoachExchange> query = db.CoachExchanges.AsNoTracking();

        if (workoutId.HasValue)
        {
            query = query.Where(e => e.WorkoutId == workoutId.Value);
        }
        else
        {
            DateOnly planMonday;
            if (!string.IsNullOrWhiteSpace(weekStartDate))
            {
                if (!DateOnly.TryParse(weekStartDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out planMonday))
                {
                    return Results.BadRequest(new { error = "weekStartDate must be yyyy-MM-dd" });
                }
            }
            else
            {
                var offset = timezoneOffsetMinutes ?? 0;
                var (monday, _, _, err) = await ResolveWeekAsync(db, null, offset, null);
                if (err != null)
                {
                    return Results.BadRequest(new { error = err });
                }

                planMonday = monday;
            }

            query = query.Where(e => e.WeekStartDate == planMonday);
        }

        var rows = await query
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();

        return Results.Ok(rows.Select(ToDocument).ToList());
    }

    private static async Task<IResult> GetExchange(
        Guid id,
        TempoDbContext db,
        CoachConfig coachConfig)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var row = await db.CoachExchanges.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
        return row == null ? Results.NotFound() : Results.Ok(ToDocument(row));
    }

    private static async Task<IResult> GetPrivacyReceipt(
        Guid id,
        TempoDbContext db,
        CoachConfig coachConfig)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var exchange = await db.CoachExchanges.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
        if (exchange == null)
        {
            return Results.NotFound();
        }

        var preflight = await db.CoachPrivacyPreflights.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == exchange.PreflightId);
        if (preflight == null)
        {
            return Results.NotFound(new { error = "Privacy receipt not found" });
        }

        object? payload = null;
        try
        {
            payload = JsonSerializer.Deserialize<object>(preflight.RedactedPayloadJson, JsonOptions);
        }
        catch (JsonException)
        {
            payload = preflight.RedactedPayloadJson;
        }

        object? report = null;
        try
        {
            report = JsonSerializer.Deserialize<object>(preflight.PrivacyReportJson, JsonOptions);
        }
        catch (JsonException)
        {
            report = preflight.PrivacyReportJson;
        }

        return Results.Ok(new CoachExchangePrivacyReceiptDocument(
            ExchangeId: exchange.Id,
            WeekStartDate: exchange.WeekStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PreflightId: preflight.Id,
            PayloadHash: preflight.PayloadHash,
            ConsentedAt: preflight.ConsentedAt,
            RedactedPayload: payload,
            PrivacyReport: report));
    }

    private static async Task<IResult> PostRetry(
        Guid id,
        [FromBody] PostCoachExchangeRetryRequest? request,
        TempoDbContext db,
        CoachConfig coachConfig,
        CoachPrivacyPreflightService preflightService,
        CoachJobService jobService)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var exchange = await db.CoachExchanges.FirstOrDefaultAsync(e => e.Id == id);
        if (exchange == null)
        {
            return Results.NotFound();
        }

        if (exchange.Status != CoachExchangeStatuses.Failed)
        {
            return Results.BadRequest(new { error = "Only failed exchanges can be retried" });
        }

        var text = exchange.AthleteText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text) && !exchange.WorkoutId.HasValue)
        {
            return Results.BadRequest(new { error = "athleteText is required when no workout is attached" });
        }

        var offset = request?.TimezoneOffsetMinutes ?? 0;
        var referenceLocal = request?.ReferenceDate != null
                             && DateOnly.TryParse(
                                 request.ReferenceDate,
                                 CultureInfo.InvariantCulture,
                                 DateTimeStyles.None,
                                 out var parsedRef)
            ? parsedRef
            : exchange.WeekStartDate;
        // Keep week assignment on the original plan week.
        var planMonday = exchange.WeekStartDate;

        var (preflight, assembleError, assembleStatus) = await preflightService.AssembleExchangeAsync(
            planMonday,
            referenceLocal,
            offset,
            text,
            exchangeId: exchange.Id,
            recordConsent: true,
            workoutId: exchange.WorkoutId,
            excludeExchangeId: exchange.Id);
        if (preflight == null)
        {
            return assembleStatus switch
            {
                400 => Results.BadRequest(new { error = assembleError }),
                _ => Results.BadRequest(new { error = assembleError ?? "Failed to assemble privacy packet" })
            };
        }

        exchange.PreflightId = preflight.Id;
        exchange.ReplyJson = null;
        exchange.SuggestedChangeJson = null;
        exchange.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var (job, jobError, jobStatus) = await jobService.StartExchangeAsync(preflight.Id, exchange.Id);
        if (job == null)
        {
            return jobStatus switch
            {
                404 => Results.NotFound(new { error = jobError }),
                409 => Results.Conflict(new { error = jobError }),
                _ => Results.BadRequest(new { error = jobError })
            };
        }

        exchange = await db.CoachExchanges.AsNoTracking().FirstAsync(e => e.Id == id);
        return Results.Json(
            new CoachExchangeSendDocument(ToJobDocument(job), ToDocument(exchange)),
            statusCode: 202);
    }

    /// <summary>
    /// When workoutId is set, week assignment uses the workout's local date (ignores referenceDate).
    /// </summary>
    private static async Task<(DateOnly PlanMonday, DateOnly ReferenceLocal, Guid? WorkoutId, string? Error)> ResolveWeekAsync(
        TempoDbContext db,
        string? referenceDate,
        int timezoneOffsetMinutes,
        Guid? workoutId)
    {
        if (workoutId.HasValue)
        {
            var workout = await db.Workouts.AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == workoutId.Value);
            if (workout == null)
            {
                return (default, default, null, "Workout not found");
            }

            var localDate = CoachAttachedRunBuilder.LocalDateFromUtc(workout.StartedAt, timezoneOffsetMinutes);
            var week = WeeklyRecapWeekMath.ResolveMondaySundayWeek(
                utcNow: DateTime.SpecifyKind(localDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
                timezoneOffsetMinutes,
                referenceLocalDate: localDate.ToDateTime(TimeOnly.MinValue));
            return (DateOnly.FromDateTime(week.WeekStartLocal), localDate, workoutId, null);
        }

        DateTime referenceLocalDt;
        if (!string.IsNullOrWhiteSpace(referenceDate))
        {
            if (!DateOnly.TryParse(referenceDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return (default, default, null, "referenceDate must be yyyy-MM-dd");
            }

            referenceLocalDt = parsed.ToDateTime(TimeOnly.MinValue);
        }
        else
        {
            var utcNow = DateTime.UtcNow;
            var local = utcNow.AddMinutes(timezoneOffsetMinutes);
            referenceLocalDt = local.Date;
        }

        var unattachedWeek = WeeklyRecapWeekMath.ResolveMondaySundayWeek(
            utcNow: DateTime.SpecifyKind(referenceLocalDt, DateTimeKind.Utc),
            timezoneOffsetMinutes,
            referenceLocalDate: referenceLocalDt);
        var planMonday = DateOnly.FromDateTime(unattachedWeek.WeekStartLocal);
        var referenceLocal = DateOnly.FromDateTime(referenceLocalDt);
        return (planMonday, referenceLocal, null, null);
    }

    private static CoachExchangeDocument ToDocument(CoachExchange exchange)
    {
        object? reply = null;
        if (!string.IsNullOrWhiteSpace(exchange.ReplyJson))
        {
            try
            {
                reply = JsonSerializer.Deserialize<object>(exchange.ReplyJson, JsonOptions);
            }
            catch (JsonException)
            {
                reply = exchange.ReplyJson;
            }
        }

        object? suggested = null;
        if (!string.IsNullOrWhiteSpace(exchange.SuggestedChangeJson))
        {
            try
            {
                suggested = JsonSerializer.Deserialize<object>(exchange.SuggestedChangeJson, JsonOptions);
            }
            catch (JsonException)
            {
                suggested = exchange.SuggestedChangeJson;
            }
        }

        return new CoachExchangeDocument(
            Id: exchange.Id,
            WeekStartDate: exchange.WeekStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            WorkoutId: exchange.WorkoutId,
            AthleteText: exchange.AthleteText,
            PreflightId: exchange.PreflightId,
            JobId: exchange.JobId,
            Status: exchange.Status,
            Reply: reply,
            SuggestedChange: suggested,
            InputTokens: exchange.InputTokens,
            OutputTokens: exchange.OutputTokens,
            ErrorCode: exchange.ErrorCode,
            ErrorMessage: exchange.ErrorMessage,
            Locked: exchange.Locked,
            CreatedAt: exchange.CreatedAt,
            UpdatedAt: exchange.UpdatedAt,
            CompletedAt: exchange.CompletedAt);
    }

    private static CoachExchangePreflightDocument ToPreflightDocument(CoachPrivacyPreflight row)
    {
        object? payload = null;
        try
        {
            payload = JsonSerializer.Deserialize<object>(row.RedactedPayloadJson, JsonOptions);
        }
        catch (JsonException)
        {
            payload = row.RedactedPayloadJson;
        }

        object? report = null;
        try
        {
            report = JsonSerializer.Deserialize<object>(row.PrivacyReportJson, JsonOptions);
        }
        catch (JsonException)
        {
            report = row.PrivacyReportJson;
        }

        return new CoachExchangePreflightDocument(
            Id: row.Id,
            Kind: row.Kind,
            WeekStartDate: row.WeekStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PayloadHash: row.PayloadHash,
            ConsentedAt: row.ConsentedAt,
            RedactedPayload: payload,
            PrivacyReport: report,
            AssembledAt: row.AssembledAt);
    }

    private static CoachJobDocument ToJobDocument(CoachJob job) =>
        new(
            Id: job.Id,
            Status: job.Status,
            Kind: string.IsNullOrWhiteSpace(job.Kind) ? CoachJobKinds.Generate : job.Kind,
            AdjustmentId: job.AdjustmentId,
            WeekStartDate: job.WeekStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PreflightId: job.PreflightId,
            ErrorCode: job.ErrorCode,
            ErrorMessage: job.ErrorMessage,
            InputTokens: job.InputTokens,
            OutputTokens: job.OutputTokens,
            SchemaRepairAttempted: job.SchemaRepairAttempted,
            ResultWeekId: job.ResultWeekId,
            CreatedAt: job.CreatedAt,
            StartedAt: job.StartedAt,
            CompletedAt: job.CompletedAt);

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

public sealed record PostCoachExchangeRequest(
    string? AthleteText,
    Guid? WorkoutId = null,
    int? TimezoneOffsetMinutes = null,
    string? ReferenceDate = null);

public sealed record PostCoachExchangeRetryRequest(
    int? TimezoneOffsetMinutes = null,
    string? ReferenceDate = null);

public sealed record CoachExchangeSendDocument(
    CoachJobDocument Job,
    CoachExchangeDocument Exchange);

public sealed record CoachExchangeDocument(
    Guid Id,
    string WeekStartDate,
    Guid? WorkoutId,
    string AthleteText,
    Guid PreflightId,
    Guid? JobId,
    string Status,
    object? Reply,
    object? SuggestedChange,
    int? InputTokens,
    int? OutputTokens,
    string? ErrorCode,
    string? ErrorMessage,
    bool Locked,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? CompletedAt);

public sealed record CoachExchangePreflightDocument(
    Guid Id,
    string Kind,
    string WeekStartDate,
    string PayloadHash,
    DateTime? ConsentedAt,
    object? RedactedPayload,
    object? PrivacyReport,
    DateTime AssembledAt);

public sealed record CoachExchangePrivacyReceiptDocument(
    Guid ExchangeId,
    string WeekStartDate,
    Guid PreflightId,
    string PayloadHash,
    DateTime? ConsentedAt,
    object? RedactedPayload,
    object? PrivacyReport);
