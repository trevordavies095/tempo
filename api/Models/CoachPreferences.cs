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

    /// <summary>
    /// Set when migration discards open draft weeks for the typed-memory upgrade.
    /// Cleared when the athlete dismisses the Coach home notice.
    /// </summary>
    public bool PendingMemoryUpgradeNotice { get; set; }

    /// <summary>
    /// Athlete dismissed the "Seed your coach's memory" home nudge.
    /// Cleared on setup reset so a wiped instance can see the nudge again.
    /// </summary>
    public bool SeedMemoryNudgeDismissed { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
