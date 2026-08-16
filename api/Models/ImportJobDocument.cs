using System.Text.Json;
using Tempo.Api.Services;

namespace Tempo.Api.Models;

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
            ErrorMessage = job.ErrorMessage
        };
    }
}
