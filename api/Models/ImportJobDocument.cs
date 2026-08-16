using System.Text.Json;
using System.Text.Json.Serialization;
using Tempo.Api.Services;

namespace Tempo.Api.Models;

public class ImportJobItemStatistics
{
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
}

public class ImportJobStatistics
{
    public ImportJobItemStatistics Settings { get; set; } = new();
    public ImportJobItemStatistics Shoes { get; set; } = new();
    public ImportJobItemStatistics Workouts { get; set; } = new();
    public ImportJobItemStatistics Routes { get; set; } = new();
    public ImportJobItemStatistics Splits { get; set; } = new();
    public ImportJobItemStatistics TimeSeries { get; set; } = new();
    public ImportJobItemStatistics Media { get; set; } = new();
    public ImportJobItemStatistics BestEfforts { get; set; } = new();
    public ImportJobItemStatistics RawFiles { get; set; } = new();
}

/// <summary>
/// Stored in ImportJob.ResultJson for tempo_export. Wire document maps Errors → errorMessages
/// so the flat int counter <c>errors</c> is not overloaded.
/// </summary>
public class ImportJobResultPayload
{
    public ImportJobStatistics Statistics { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class ImportJobDocument
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public long ByteSize { get; set; }
    public long BytesReceived { get; set; }
    public int Processed { get; set; }
    public int Total { get; set; }
    public int Successful { get; set; }
    public int Skipped { get; set; }
    public int Updated { get; set; }
    public int Errors { get; set; }
    public List<StravaBulkImportError> ErrorDetails { get; set; } = new();
    public string? ErrorMessage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImportJobStatistics? Statistics { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Warnings { get; set; }

    /// <summary>
    /// Tempo export string errors from ResultJson (avoids clashing with flat int <see cref="Errors"/>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ErrorMessages { get; set; }

    public static ImportJobDocument FromEntity(ImportJob job)
    {
        var details = new List<StravaBulkImportError>();
        if (!string.IsNullOrWhiteSpace(job.ErrorDetailsJson))
        {
            try
            {
                details = JsonSerializer.Deserialize<List<StravaBulkImportError>>(job.ErrorDetailsJson, JsonOptions)
                    ?? new List<StravaBulkImportError>();
            }
            catch (JsonException)
            {
                details = new List<StravaBulkImportError>();
            }
        }

        ImportJobStatistics? statistics = null;
        List<string>? warnings = null;
        List<string>? errorMessages = null;
        if (!string.IsNullOrWhiteSpace(job.ResultJson))
        {
            try
            {
                var payload = JsonSerializer.Deserialize<ImportJobResultPayload>(job.ResultJson, JsonOptions);
                if (payload != null)
                {
                    statistics = payload.Statistics;
                    warnings = payload.Warnings;
                    errorMessages = payload.Errors;
                }
            }
            catch (JsonException)
            {
                // Leave Tempo payload fields null on corrupt JSON.
            }
        }

        return new ImportJobDocument
        {
            Id = job.Id,
            Kind = job.Kind,
            Status = job.Status,
            Filename = job.Filename,
            ByteSize = job.ByteSize,
            BytesReceived = job.BytesReceived,
            Processed = job.Processed,
            Total = job.Total,
            Successful = job.Successful,
            Skipped = job.Skipped,
            Updated = job.Updated,
            Errors = job.Errors,
            ErrorDetails = details,
            ErrorMessage = job.ErrorMessage,
            Statistics = statistics,
            Warnings = warnings,
            ErrorMessages = errorMessages
        };
    }
}
