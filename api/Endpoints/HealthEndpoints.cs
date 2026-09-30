using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;

namespace Tempo.Api.Endpoints;

public static class HealthEndpoints
{
    private static readonly TimeSpan ReadyProbeTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Liveness: the API process is accepting HTTP.
    /// </summary>
    /// <remarks>
    /// Does not touch Postgres, media, or backfills. May return 200 while the database is down.
    /// </remarks>
    private static IResult GetHealth() =>
        Results.Ok(new { status = "healthy" });

    /// <summary>
    /// Readiness: this instance can serve because Postgres answers.
    /// </summary>
    /// <remarks>
    /// Returns 200 when CanConnectAsync succeeds within 2 seconds; otherwise 503.
    /// JSON names the database check; never includes connection strings or exception text.
    /// </remarks>
    private static async Task<IResult> GetReady(TempoDbContext db, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Tempo.Api.Health");
        try
        {
            db.Database.SetCommandTimeout(ReadyProbeTimeout);
            using var cts = new CancellationTokenSource(ReadyProbeTimeout);
            var canConnect = await db.Database.CanConnectAsync(cts.Token);
            if (canConnect)
            {
                return Results.Ok(new
                {
                    status = "ready",
                    checks = new { database = "ok" }
                });
            }
        }
        catch (Exception)
        {
            // Intentionally omit exception: Npgsql messages often include host.
        }

        logger.LogWarning("Readiness check failed: database");
        return Results.Json(
            new
            {
                status = "not_ready",
                checks = new { database = "fail" }
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health", GetHealth)
            .WithTags("Health")
            .WithName("GetHealth")
            .WithSummary("Liveness check")
            .WithDescription("Returns 200 while the API process is accepting HTTP. Does not check the database.")
            .Produces(200);

        app.MapGet("/ready", GetReady)
            .WithTags("Health")
            .WithName("GetReady")
            .WithSummary("Readiness check")
            .WithDescription("Returns 200 when Postgres is reachable within 2 seconds; otherwise 503. Orchestrators should use this URL.")
            .Produces(200)
            .Produces(503);
    }
}
