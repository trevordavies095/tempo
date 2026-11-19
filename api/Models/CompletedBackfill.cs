using System.ComponentModel.DataAnnotations;

namespace Tempo.Api.Models;

/// <summary>
/// Instance-local record that an upgrade backfill finished with nothing left to do, so later API
/// starts skip it (#218). One row per backfill; a different <see cref="Version"/> means the backfill
/// changed and runs again. Cleared after a Tempo ZIP restore. Not exported.
/// </summary>
public class CompletedBackfill
{
    [Key]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public int Version { get; set; }

    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}
