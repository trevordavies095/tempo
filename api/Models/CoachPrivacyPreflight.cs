using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Tempo.Api.Models;

/// <summary>
/// Peelable privacy pre-flight artifact for a weekly ritual or an exchange.
/// Stores redacted packet JSON only. ConsentedAt null until the athlete confirms.
/// </summary>
public class CoachPrivacyPreflight
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary><see cref="CoachPrivacyPreflightKinds.Weekly"/> or <see cref="CoachPrivacyPreflightKinds.Exchange"/>.</summary>
    [Required]
    [MaxLength(32)]
    public string Kind { get; set; } = CoachPrivacyPreflightKinds.Weekly;

    /// <summary>Local Monday of the plan / exchange week.</summary>
    public DateOnly WeekStartDate { get; set; }

    /// <summary>Set for exchange prefights; null for weekly.</summary>
    public Guid? ExchangeId { get; set; }

    public int TimezoneOffsetMinutes { get; set; }

    /// <summary>Reference local date used when assembling (yyyy-MM-dd semantics).</summary>
    public DateOnly ReferenceDate { get; set; }

    [Required]
    [Column(TypeName = "jsonb")]
    public string RedactedPayloadJson { get; set; } = "{}";

    [Required]
    [Column(TypeName = "jsonb")]
    public string PrivacyReportJson { get; set; } = "{}";

    /// <summary>SHA-256 hex of RedactedPayloadJson.</summary>
    [Required]
    [MaxLength(64)]
    public string PayloadHash { get; set; } = string.Empty;

    public DateTime AssembledAt { get; set; } = DateTime.UtcNow;

    /// <summary>Null until athlete consents. Only consented rows are sendable.</summary>
    public DateTime? ConsentedAt { get; set; }
}
