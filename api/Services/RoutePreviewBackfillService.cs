using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;

namespace Tempo.Api.Services;

/// <summary>
/// Fills <c>WorkoutRoutes.PreviewGeoJson</c> for rows that still have a null preview.
/// Idempotent: sentinel (<c>[]</c>) previews are left alone; a fully backfilled database is one count query.
/// </summary>
public class RoutePreviewBackfillService
{
    public const int BatchSize = 200;

    private readonly TempoDbContext _db;
    private readonly ILogger<RoutePreviewBackfillService> _logger;

    public RoutePreviewBackfillService(
        TempoDbContext db,
        ILogger<RoutePreviewBackfillService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Computes and saves previews for every route whose preview is still null.
    /// Returns the number of rows updated.
    /// </summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var total = await _db.WorkoutRoutes
            .CountAsync(r => r.PreviewGeoJson == null, cancellationToken);

        if (total == 0)
        {
            _logger.LogInformation("Workout route preview backfill: {Processed} of {Total}", 0, 0);
            return 0;
        }

        var processed = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await _db.WorkoutRoutes
                .Where(r => r.PreviewGeoJson == null)
                .OrderBy(r => r.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            foreach (var route in batch)
            {
                route.PreviewGeoJson = TrackGeometry.BuildRoutePreviewGeoJson(route.RouteGeoJson);
            }

            await _db.SaveChangesAsync(cancellationToken);
            processed += batch.Count;
            _logger.LogInformation(
                "Workout route preview backfill: {Processed} of {Total}",
                processed,
                total);
            _db.ChangeTracker.Clear();
        }

        return processed;
    }
}
