using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Tempo.Api.Models;

/// <summary>
/// Peelable Layer 3 standing rule. Model proposes; human promotes/revises/retires.
/// No FK into core Tempo tables.
/// </summary>
public class CoachRule
{
    /// <summary>Stable id: rule_{slug}</summary>
    [Key]
    [MaxLength(120)]
    public string Id { get; set; } = string.Empty;

    /// <summary>data_interpretation_notes | key_learnings | coaching_preferences</summary>
    [Required]
    [MaxLength(60)]
    public string TargetLayer { get; set; } = string.Empty;

    [Required]
    public string RuleText { get; set; } = string.Empty;

    /// <summary>active | retired</summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = CoachRuleStatuses.Active;

    [Required]
    [Column(TypeName = "jsonb")]
    public List<string> Evidence { get; set; } = [];

    /// <summary>coach | athlete</summary>
    [Required]
    [MaxLength(20)]
    public string Source { get; set; } = CoachRuleSources.Coach;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public static class CoachRuleStatuses
{
    public const string Active = "active";
    public const string Retired = "retired";
}

public static class CoachRuleSources
{
    public const string Coach = "coach";
    public const string Athlete = "athlete";
}

public static class CoachRuleTargetLayers
{
    public const string DataInterpretationNotes = "data_interpretation_notes";
    public const string KeyLearnings = "key_learnings";
    public const string CoachingPreferences = "coaching_preferences";
}
