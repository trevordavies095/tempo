using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Tempo.Api.Models;

/// <summary>
/// Per-point time series data for charting and analysis.
/// Stores metrics at regular intervals (typically 1-second or per-track-point).
/// </summary>
public class WorkoutTimeSeries
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid WorkoutId { get; set; }

    /// <summary>
    /// Elapsed seconds from workout start
    /// </summary>
    [Required]
    public int ElapsedSeconds { get; set; }

    /// <summary>
    /// Cumulative distance at this point (meters)
    /// </summary>
    [Column(TypeName = "double precision")]
    public double? DistanceM { get; set; }

    /// <summary>
    /// Heart rate at this point (bpm)
    /// </summary>
    public byte? HeartRateBpm { get; set; }

    /// <summary>
    /// Cadence at this point (rpm)
    /// </summary>
    public byte? CadenceRpm { get; set; }

    /// <summary>
    /// Power at this point (watts)
    /// </summary>
    public ushort? PowerWatts { get; set; }

    /// <summary>
    /// Speed at this point (meters per second)
    /// </summary>
    [Column(TypeName = "double precision")]
    public double? SpeedMps { get; set; }

    /// <summary>
    /// Grade at this point (percentage)
    /// </summary>
    [Column(TypeName = "double precision")]
    public double? GradePercent { get; set; }

    /// <summary>
    /// Elevation at this point (meters)
    /// </summary>
    [Column(TypeName = "double precision")]
    public double? ElevationM { get; set; }

    /// <summary>
    /// Temperature at this point (°C)
    /// </summary>
    public sbyte? TemperatureC { get; set; }

    /// <summary>
    /// Vertical speed at this point (meters per second)
    /// </summary>
    [Column(TypeName = "double precision")]
    public double? VerticalSpeedMps { get; set; }

    // Navigation property
    [ForeignKey(nameof(WorkoutId))]
    public Workout Workout { get; set; } = null!;
}

