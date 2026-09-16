using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;

namespace Tempo.Api.Endpoints;

public static class SettingsIntervalsIcuEndpoints
{
    public static RouteGroupBuilder MapIntervalsIcuSettings(this RouteGroupBuilder group)
    {
        group.MapGet("/intervals-icu", GetIntervalsIcuConnection)
            .WithName("GetIntervalsIcuConnection")
            .Produces<IntervalsIcuConnectionDocument>(200)
            .WithSummary("Get intervals.icu connection status")
            .WithDescription(
                "Returns whether an Intervals.icu connection exists. Never returns the API key. " +
                "When disconnected, connected is false.");

        group.MapPut("/intervals-icu", PutIntervalsIcuConnection)
            .WithName("PutIntervalsIcuConnection")
            .Accepts<PutIntervalsIcuConnectionRequest>("application/json")
            .Produces<IntervalsIcuConnectionDocument>(200)
            .Produces(400)
            .Produces(409)
            .Produces(503)
            .WithSummary("Connect intervals.icu")
            .WithDescription(
                "Probes intervals.icu as athlete 0, then stores the API key encrypted. " +
                "A second connect while already connected is 409.");

        group.MapDelete("/intervals-icu", DeleteIntervalsIcuConnection)
            .WithName("DeleteIntervalsIcuConnection")
            .Produces(204)
            .WithSummary("Disconnect intervals.icu")
            .WithDescription("Deletes the connection row. Workouts and external identities stay. 204 if already gone.");

        group.MapPost("/intervals-icu/sync", PostIntervalsIcuSync)
            .WithName("PostIntervalsIcuSync")
            .Produces(202)
            .Produces(204)
            .WithSummary("Wake intervals.icu sync")
            .WithDescription(
                "Enqueues a live sync tick. Returns 202 when connected and enabled. " +
                "Returns 204 when there is no connection or sync is disabled. Does not import on the request thread.");

        return group;
    }

    private static async Task<IResult> GetIntervalsIcuConnection(TempoDbContext db)
    {
        var row = await db.IntervalsIcuConnections.AsNoTracking().FirstOrDefaultAsync();
        return Results.Ok(ToDocument(row));
    }

    private static async Task<IResult> PutIntervalsIcuConnection(
        [FromBody] PutIntervalsIcuConnectionRequest? request,
        TempoDbContext db,
        IIntervalsIcuClient intervalsIcu,
        IntervalsIcuSecretProtector protector,
        ILogger<Program> logger)
    {
        var apiKey = request?.ApiKey?.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            return Results.BadRequest(new { error = "apiKey is required" });
        }

        var existing = await db.IntervalsIcuConnections.FirstOrDefaultAsync();
        if (existing != null)
        {
            return Results.Conflict(new { error = "Already connected. Disconnect first." });
        }

        var probe = await intervalsIcu.ProbeAthleteAsync(apiKey);
        if (probe == IntervalsIcuProbeResult.Unauthorized)
        {
            return Results.BadRequest(new { error = "intervals.icu rejected the API key" });
        }

        if (probe == IntervalsIcuProbeResult.Transient)
        {
            return Results.Json(
                new { error = "Could not reach intervals.icu. Try again." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var now = DateTime.UtcNow;
        var row = new IntervalsIcuConnection
        {
            ApiKeyCiphertext = protector.Encrypt(apiKey),
            Enabled = true,
            ConnectedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.IntervalsIcuConnections.Add(row);
        await db.SaveChangesAsync();

        logger.LogInformation("Intervals.icu connection saved");
        return Results.Ok(ToDocument(row));
    }

    private static async Task<IResult> DeleteIntervalsIcuConnection(TempoDbContext db)
    {
        var rows = await db.IntervalsIcuConnections.ToListAsync();
        if (rows.Count > 0)
        {
            db.IntervalsIcuConnections.RemoveRange(rows);
            await db.SaveChangesAsync();
        }

        return Results.NoContent();
    }

    private static async Task<IResult> PostIntervalsIcuSync(
        TempoDbContext db,
        IntervalsIcuSyncQueue queue)
    {
        var row = await db.IntervalsIcuConnections.AsNoTracking().FirstOrDefaultAsync();
        if (row == null || !row.Enabled)
        {
            return Results.NoContent();
        }

        queue.TryWake();
        return Results.StatusCode(StatusCodes.Status202Accepted);
    }

    private static IntervalsIcuConnectionDocument ToDocument(IntervalsIcuConnection? row)
    {
        if (row == null)
        {
            return new IntervalsIcuConnectionDocument { Connected = false };
        }

        return new IntervalsIcuConnectionDocument
        {
            Connected = true,
            Enabled = row.Enabled,
            LastSuccessfulSyncAt = row.LastSuccessfulSyncAt,
            LastSyncAttemptAt = row.LastSyncAttemptAt,
            LastError = row.LastError
        };
    }
}

public sealed class PutIntervalsIcuConnectionRequest
{
    public string? ApiKey { get; set; }
}

public sealed class IntervalsIcuConnectionDocument
{
    public bool Connected { get; init; }
    public bool? Enabled { get; init; }
    public DateTime? LastSuccessfulSyncAt { get; init; }
    public DateTime? LastSyncAttemptAt { get; init; }
    public string? LastError { get; init; }
}
