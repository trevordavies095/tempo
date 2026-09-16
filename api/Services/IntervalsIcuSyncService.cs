using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

public sealed class IntervalsIcuSyncService
{
    private readonly TempoDbContext _db;
    private readonly IIntervalsIcuClient _client;
    private readonly IntervalsIcuSecretProtector _protector;
    private readonly WorkoutIntake _intake;
    private readonly ILogger<IntervalsIcuSyncService> _logger;

    public IntervalsIcuSyncService(
        TempoDbContext db,
        IIntervalsIcuClient client,
        IntervalsIcuSecretProtector protector,
        WorkoutIntake intake,
        ILogger<IntervalsIcuSyncService> logger)
    {
        _db = db;
        _client = client;
        _protector = protector;
        _intake = intake;
        _logger = logger;
    }

    public async Task RunTickAsync(CancellationToken cancellationToken = default)
    {
        var row = await _db.IntervalsIcuConnections.FirstOrDefaultAsync(cancellationToken);
        if (row == null || !row.Enabled)
        {
            return;
        }

        var now = DateTime.UtcNow;
        row.LastSyncAttemptAt = now;
        row.UpdatedAt = now;

        string apiKey;
        try
        {
            apiKey = _protector.Decrypt(row.ApiKeyCiphertext);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not decrypt intervals.icu API key");
            row.LastError = "Could not decrypt the stored API key.";
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        var oldest = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2);
        var list = await _client.ListActivitiesAsync(apiKey, oldest, cancellationToken);
        if (list.Status != IntervalsIcuProbeResult.Ok)
        {
            row.LastError = list.Status == IntervalsIcuProbeResult.Unauthorized
                ? "intervals.icu rejected the API key"
                : "Could not list intervals.icu activities.";
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        foreach (var activity in list.Activities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsRunningType(activity.Type) || !IsFitOrGpx(activity.FileType))
            {
                continue;
            }

            var file = await _client.GetActivityFileAsync(apiKey, activity.Id, cancellationToken);
            if (file == null || file.Bytes.Length == 0)
            {
                continue;
            }

            var fileName = ResolveFileName(file.FileName, activity.FileType);
            await using var stream = new MemoryStream(file.Bytes, writable: false);
            var overlay = new WorkoutIntakeOverlay
            {
                Source = WorkoutExternalSource.IntervalsIcu,
                ExternalIdentity = new WorkoutIntakeExternalIdentity
                {
                    Source = WorkoutExternalSource.IntervalsIcu,
                    ExternalId = activity.Id
                }
            };

            var result = await _intake.ProcessAsync(stream, fileName, overlay);
            if (result.ErrorMessage != null)
            {
                _logger.LogWarning(
                    "intervals.icu activity {ActivityId} persist failed: {Error}",
                    activity.Id,
                    result.ErrorMessage);
            }
        }

        row.LastSuccessfulSyncAt = DateTime.UtcNow;
        row.LastError = null;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    internal static bool IsRunningType(string? type)
        => !string.IsNullOrWhiteSpace(type)
           && (type.Equals("Run", StringComparison.Ordinal)
               || type.EndsWith("Run", StringComparison.Ordinal));

    internal static bool IsFitOrGpx(string? fileType)
    {
        if (string.IsNullOrWhiteSpace(fileType))
        {
            return false;
        }

        var normalized = fileType.Trim().TrimStart('.').ToLowerInvariant();
        return normalized is "fit" or "fit.gz" or "gpx" or "gpx.gz";
    }

    private static string ResolveFileName(string? fileName, string? fileType)
    {
        if (!string.IsNullOrWhiteSpace(fileName)
            && (fileName.EndsWith(".fit", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".fit.gz", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".gpx", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".gpx.gz", StringComparison.OrdinalIgnoreCase)))
        {
            return fileName;
        }

        if (IsFitOrGpx(fileType))
        {
            var ext = fileType!.Trim().TrimStart('.').ToLowerInvariant();
            ext = ext switch
            {
                "fit.gz" => "fit",
                "gpx.gz" => "gpx",
                _ => ext
            };
            return $"activity.{ext}";
        }

        return "activity.fit";
    }
}
