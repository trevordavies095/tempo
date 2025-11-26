using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Tempo.Api.Models;

public class Workout
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    // ============================================
    // CORE STATS (Dedicated Columns)
    // ============================================
    
    [Required]
    public DateTime StartedAt { get; set; }

    [Required]
    public int DurationS { get; set; }  // Total elapsed time

    [Required]
    [Column(TypeName = "double precision")]
    public double DistanceM { get; set; }

    [Required]
    public int AvgPaceS { get; set; }  // Average pace in seconds/km

    [Column(TypeName = "double precision")]
    public double? ElevGainM { get; set; }

    // Optional core stats (can be extracted from JSONB later if needed)
    [Column(TypeName = "double precision")]
    public double? ElevLossM { get; set; }

    [Column(TypeName = "double precision")]
    public double? MinElevM { get; set; }

    [Column(TypeName = "double precision")]
    public double? MaxElevM { get; set; }

    [Column(TypeName = "double precision")]
    public double? MaxSpeedMps { get; set; }  // meters per second

    [Column(TypeName = "double precision")]
    public double? AvgSpeedMps { get; set; }

    public int? MovingTimeS { get; set; }  // Excludes pause time

    // Heart rate (if available)
    public byte? MaxHeartRateBpm { get; set; }
    public byte? AvgHeartRateBpm { get; set; }
    public byte? MinHeartRateBpm { get; set; }

    // Cadence (if available)
    public byte? MaxCadenceRpm { get; set; }
    public byte? AvgCadenceRpm { get; set; }

    // Power (if available)
    public ushort? MaxPowerWatts { get; set; }
    public ushort? AvgPowerWatts { get; set; }

    // Calories (if available)
    public ushort? Calories { get; set; }

    // Relative Effort (calculated from heart rate zones)
    public int? RelativeEffort { get; set; }

    // ============================================
    // METADATA
    // ============================================

    [MaxLength(200)]
    [Column(TypeName = "text")]
    public string? Name { get; set; }

    [MaxLength(50)]
    public string? RunType { get; set; }  // easy, tempo, long, race, recovery

    [Column(TypeName = "text")]
    public string? Notes { get; set; }

    [MaxLength(50)]
    public string? Source { get; set; }  // apple_watch, strava_import, garmin_import

    [MaxLength(100)]
    [Column(TypeName = "text")]
    public string? Device { get; set; }  // Device used to record workout (e.g., "Garmin Forerunner 945", "Apple Watch Series 9")

    [Column(TypeName = "bytea")]
    public byte[]? RawFileData { get; set; }

    [MaxLength(255)]
    public string? RawFileName { get; set; }

    [MaxLength(10)]
    public string? RawFileType { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ============================================
    // RAW DATA (JSONB Fields)
    // ============================================

    /// <summary>
    /// Raw GPX data: metadata, extensions, all track points, calculated metrics
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string? RawGpxData { get; set; }

    /// <summary>
    /// Raw FIT data: All SessionMesg fields, RecordMesg summary, device info
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string? RawFitData { get; set; }

    /// <summary>
    /// Raw Strava CSV data: All columns from activities.csv not already mapped
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string? RawStravaData { get; set; }

    /// <summary>
    /// Weather data: Open-Meteo API response + FIT/Strava weather if available
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string? Weather { get; set; }

    // ============================================
    // NAVIGATION PROPERTIES
    // ============================================

    public WorkoutRoute? Route { get; set; }
    public ICollection<WorkoutSplit> Splits { get; set; } = new List<WorkoutSplit>();
    public ICollection<WorkoutMedia> Media { get; set; } = new List<WorkoutMedia>();
    public ICollection<WorkoutTimeSeries> TimeSeries { get; set; } = new List<WorkoutTimeSeries>();
}

