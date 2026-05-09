using System.ComponentModel.DataAnnotations;

namespace Tempo.Api.Models;

public class ApiKey
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    [MaxLength(200)]
    public string? Label { get; set; }

    [Required]
    public string KeyHash { get; set; } = string.Empty;

    [Required]
    [MaxLength(64)]
    public string KeyPrefix { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? RevokedAt { get; set; }

    public User User { get; set; } = null!;
}
