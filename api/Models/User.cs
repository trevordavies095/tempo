using System.ComponentModel.DataAnnotations;

namespace Tempo.Api.Models;

public class User
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Incremented on password change to invalidate outstanding JWTs (see <c>sess_ver</c> claim).
    /// </summary>
    public int SessionVersion { get; set; }

    /// <summary>
    /// True after first-run onboarding finishes. New registrations default to false;
    /// existing users are backfilled to true by migration.
    /// </summary>
    public bool OnboardingCompleted { get; set; }

    public ICollection<ApiKey> ApiKeys { get; set; } = new List<ApiKey>();
}

