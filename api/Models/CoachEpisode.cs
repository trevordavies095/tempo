using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Tempo.Api.Models;

/// <summary>
/// Peelable Layer 2 episode (significant training events). Append-only; soft-retire.
/// No FK into core Tempo tables. SourceWeekId is a soft reference to CoachWeek.
/// </summary>
public class CoachEpisode
{
    /// <summary>Stable id: ep_{date}_{category}_{slug}</summary>
    [Key]
    [MaxLength(120)]
    public string Id { get; set; } = string.Empty;

    /// <summary>When the event happened (not when logged).</summary>
    public DateOnly Date { get; set; }

    /// <summary>race | breakthrough | issue | equipment | milestone | protocol_change | recap</summary>
    [Required]
    [MaxLength(40)]
    public string Category { get; set; } = string.Empty;

    [Required]
    [MaxLength(400)]
    public string Summary { get; set; } = string.Empty;

    /// <summary>Sunday end of the Mon–Sun week that produced the episode.</summary>
    public DateOnly? SourceWeekEnding { get; set; }

    /// <summary>Soft reference to CoachWeek.Id (no FK).</summary>
    public Guid? SourceWeekId { get; set; }

    /// <summary>coach | athlete</summary>
    [Required]
    [MaxLength(20)]
    public string Source { get; set; } = CoachEpisodeSources.Coach;

    public bool Retired { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class CoachEpisodeSources
{
    public const string Coach = "coach";
    public const string Athlete = "athlete";
}

public static class CoachEpisodeCategories
{
    public const string Race = "race";
    public const string Breakthrough = "breakthrough";
    public const string Issue = "issue";
    public const string Equipment = "equipment";
    public const string Milestone = "milestone";
    public const string ProtocolChange = "protocol_change";
    public const string Recap = "recap";
}
