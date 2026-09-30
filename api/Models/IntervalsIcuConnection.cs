using System.ComponentModel.DataAnnotations;

namespace Tempo.Api.Models;

/// <summary>
/// Instance-level 0-or-1 row linking Tempo to the authenticated intervals.icu athlete (always id 0).
/// Not UserSettings, not Tempo ApiKey, not an ImportJob.
/// </summary>
public class IntervalsIcuConnection
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public byte[] ApiKeyCiphertext { get; set; } = [];

    public bool Enabled { get; set; } = true;

    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastSuccessfulSyncAt { get; set; }

    public DateTime? LastSyncAttemptAt { get; set; }

    [MaxLength(500)]
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
