using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Tempo.Api.Models;

public enum HeartRateCalculationMethod
{
    AgeBased,   // 220 - age
    Karvonen,   // Heart Rate Reserve method
    Custom      // User-defined zones
}

public class HeartRateZone
{
    public int MinBpm { get; set; }
    public int MaxBpm { get; set; }
}

public class UserSettings
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public HeartRateCalculationMethod CalculationMethod { get; set; } = HeartRateCalculationMethod.AgeBased;

    // For AgeBased method
    public int? Age { get; set; }

    // For Karvonen method
    public int? RestingHeartRateBpm { get; set; }
    public int? MaxHeartRateBpm { get; set; }

    // Zone boundaries (5 zones)
    // Zone 1: 50-60%, Zone 2: 60-70%, Zone 3: 70-80%, Zone 4: 80-90%, Zone 5: 90-100%
    public int Zone1MinBpm { get; set; }
    public int Zone1MaxBpm { get; set; }
    public int Zone2MinBpm { get; set; }
    public int Zone2MaxBpm { get; set; }
    public int Zone3MinBpm { get; set; }
    public int Zone3MaxBpm { get; set; }
    public int Zone4MinBpm { get; set; }
    public int Zone4MaxBpm { get; set; }
    public int Zone5MinBpm { get; set; }
    public int Zone5MaxBpm { get; set; }

    [MaxLength(20)]
    public string? UnitPreference { get; set; } // "metric" or "imperial", default "metric"

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

