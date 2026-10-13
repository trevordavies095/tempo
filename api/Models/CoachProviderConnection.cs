using System.ComponentModel.DataAnnotations;

namespace Tempo.Api.Models;

/// <summary>
/// Instance-level 0-or-1 row for the Coach Anthropic BYO API key.
/// Peelable with the coach module. Not UserSettings, not CoachPreferences.
/// </summary>
public class CoachProviderConnection
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public byte[] ApiKeyCiphertext { get; set; } = [];

    [Required]
    [MaxLength(100)]
    public string ModelId { get; set; } = string.Empty;

    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastProbeAt { get; set; }

    [MaxLength(500)]
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
