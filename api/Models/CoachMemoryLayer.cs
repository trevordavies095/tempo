using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Tempo.Api.Models;

/// <summary>
/// Peelable coach memory document for Layer 1 / 2 / 3.
/// One row per layer key. No FK into core Tempo tables.
/// </summary>
public class CoachMemoryLayer
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>L1 | L2 | L3</summary>
    [Required]
    [MaxLength(10)]
    public string Layer { get; set; } = string.Empty;

    [Required]
    [Column(TypeName = "jsonb")]
    public string Document { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
