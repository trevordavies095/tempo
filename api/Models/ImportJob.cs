using System.ComponentModel.DataAnnotations;

namespace Tempo.Api.Models;

public static class ImportJobKinds
{
    public const string StravaBulk = "strava_bulk";
}

public static class ImportJobStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public class ImportJob
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(32)]
    public string Kind { get; set; } = ImportJobKinds.StravaBulk;

    [Required]
    [MaxLength(32)]
    public string Status { get; set; } = ImportJobStatuses.Queued;

    [MaxLength(260)]
    public string Filename { get; set; } = string.Empty;

    public long ByteSize { get; set; }

    public long BytesReceived { get; set; }

    public int Processed { get; set; }

    public int Total { get; set; }

    public int Successful { get; set; }

    public int Skipped { get; set; }

    public int Updated { get; set; }

    public int Errors { get; set; }

    /// <summary>
    /// JSON array of { filename, error }.
    /// </summary>
    public string? ErrorDetailsJson { get; set; }

    public string? ErrorMessage { get; set; }

    [MaxLength(20)]
    public string? UnitPreference { get; set; }

    public string? ArchivePath { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }
}
