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
            .Produces(503)
            .WithSummary("Connect or replace intervals.icu key")
            .WithDescription(
                "Probes intervals.icu as athlete 0, then stores the API key encrypted. " +
                "A second PUT while connected replaces the ciphertext only and keeps connectedAt / lastSuccessfulSyncAt.");

        group.MapDelete("/intervals-icu", DeleteIntervalsIcuConnection)
            .WithName("DeleteIntervalsIcuConnection")
            .Produces(204)
            .WithSummary("Disconnect intervals.icu")
            .WithDescription("Deletes the connection row. Workouts and external identities stay. 204 if already gone.");

        group.MapPost("/intervals-icu/enable", PostIntervalsIcuEnable)
            .WithName("PostIntervalsIcuEnable")
            .Produces<IntervalsIcuConnectionDocument>(200)
            .Produces(400)
            .Produces(404)
            .Produces(503)
            .WithSummary("Re-enable intervals.icu sync")
            .WithDescription(
                "Probes with the stored key and turns live sync back on. " +
                "404 if there is no connection. 400 if decrypt or probe fails.");

        group.MapPost("/intervals-icu/sync", PostIntervalsIcuSync)
            .WithName("PostIntervalsIcuSync")
            .Produces(202)
            .Produces(204)
            .WithSummary("Wake intervals.icu sync")
            .WithDescription(
                "Enqueues a live sync tick. Returns 202 when connected and enabled, including coalesced wakes. " +
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
        var existing = await db.IntervalsIcuConnections.FirstOrDefaultAsync();
        if (existing != null)
        {
            existing.ApiKeyCiphertext = protector.Encrypt(apiKey);
            existing.Enabled = true;
            existing.LastError = null;
            existing.UpdatedAt = now;
            await db.SaveChangesAsync();
            logger.LogInformation("Intervals.icu API key replaced");
            return Results.Ok(ToDocument(existing));
        }

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

    private static async Task<IResult> PostIntervalsIcuEnable(
        TempoDbContext db,
        IIntervalsIcuClient intervalsIcu,
        IntervalsIcuSecretProtector protector)
    {
        var row = await db.IntervalsIcuConnections.FirstOrDefaultAsync();
        if (row == null)
        {
            return Results.NotFound(new { error = "Not connected" });
        }

        string apiKey;
        try
        {
            apiKey = protector.Decrypt(row.ApiKeyCiphertext);
        }
        catch
        {
            return Results.BadRequest(new { error = "Could not decrypt the stored API key." });
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

        row.Enabled = true;
        row.LastError = null;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
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
