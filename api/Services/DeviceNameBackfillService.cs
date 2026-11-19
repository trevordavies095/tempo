using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Renames <see cref="Workout.Device"/> on FIT workouts that an older Tempo labelled with only a
/// brand ("Garmin", "Coros", ...) or "Product N", by running <see cref="DeviceExtractionService"/>
/// again on the device info stored in <see cref="Workout.RawFitData"/>. Any other name (a product
/// name string, HealthKit, "Apple Watch") is never touched. Idempotent: a renamed row no longer
/// matches, and a row whose name would not change is skipped without a write.
/// </summary>
public class DeviceNameBackfillService
{
    public const int BatchSize = 200;

    /// <summary>Every name the manufacturer-only fallback produced before the SDK-backed tables.</summary>
    public static readonly string[] LegacyGenericNames =
    [
        "Garmin", "Suunto", "Wahoo Fitness", "TomTom", "Wattbike", "Stryd", "Polar", "Coros",
        "Tag Heuer", "Zwift", "Strava",
    ];

    public const string LegacyProductPrefix = "Product ";

    private readonly TempoDbContext _db;
    private readonly ILogger<DeviceNameBackfillService> _logger;

    public DeviceNameBackfillService(TempoDbContext db, ILogger<DeviceNameBackfillService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Returns the number of workouts renamed.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var candidateIds = await _db.Workouts
            .AsNoTracking()
            .Where(w => w.RawFitData != null && w.Device != null &&
                        (LegacyGenericNames.Contains(w.Device) || w.Device.StartsWith(LegacyProductPrefix)))
            .OrderBy(w => w.Id)
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);

        // Logged total counts rows whose name would change, so a finished library logs "0 of 0"
        // even though brand-only rows with no better name still match the scan.
        var renamed = 0;
        var needed = 0;
        foreach (var batch in candidateIds.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rows = await _db.Workouts
                .AsNoTracking()
                .Where(w => batch.Contains(w.Id))
                .OrderBy(w => w.Id)
                .Select(w => new { w.Id, w.Device, w.RawFitData })
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                string? name;
                try
                {
                    name = ExtractFromRawFitData(row.RawFitData);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Device name backfill: RawFitData unreadable for workout {WorkoutId}", row.Id);
                    continue;
                }

                if (name == null || name == row.Device)
                {
                    continue;
                }

                needed++;

                // Only rewrite when the row still holds the name we read, so a concurrent import wins.
                renamed += await _db.Workouts
                    .Where(w => w.Id == row.Id && w.Device == row.Device)
                    .ExecuteUpdateAsync(s => s.SetProperty(w => w.Device, name), cancellationToken);
            }
        }

        _logger.LogInformation("Device name backfill: {Renamed} of {Total}", renamed, needed);
        return renamed;
    }

    private static string? ExtractFromRawFitData(string? rawFitData)
    {
        if (string.IsNullOrEmpty(rawFitData))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(rawFitData);
        return doc.RootElement.ValueKind == JsonValueKind.Object &&
               doc.RootElement.TryGetProperty("device", out var device) &&
               device.ValueKind == JsonValueKind.Object
            ? DeviceExtractionService.ExtractDeviceName(device)
            : null;
    }
}
