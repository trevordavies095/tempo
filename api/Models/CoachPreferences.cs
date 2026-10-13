using System.ComponentModel.DataAnnotations;

namespace Tempo.Api.Models;

/// <summary>
/// Instance-level 0-or-1 row for athlete AI Coach opt-in.
/// Not UserSettings, not User.OnboardingCompleted. Peelable with the coach module.
/// </summary>
public class CoachPreferences
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public bool OptedIn { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
