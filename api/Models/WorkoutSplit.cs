using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Tempo.Api.Models;

public static class WorkoutSplitKinds
{
    public const string Distance = "distance";
    public const string DeviceLap = "device_lap";
}

public class WorkoutSplit
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid WorkoutId { get; set; }

    /// <summary>
    /// Segment kind: <see cref="WorkoutSplitKinds.Distance"/> (km/mile) or
    /// <see cref="WorkoutSplitKinds.DeviceLap"/> (device range). Idx is unique per kind.
    /// </summary>
    [Required]
    [MaxLength(32)]
    public string Kind { get; set; } = WorkoutSplitKinds.Distance;

    [Required]
    public int Idx { get; set; }

    [Required]
    [Column(TypeName = "double precision")]
    public double DistanceM { get; set; }

    [Required]
    public int DurationS { get; set; }

    [Required]
    [Column(TypeName = "double precision")]
    public double PaceS { get; set; }

    /// <summary>
    /// Wall elapsed seconds from stored Workout.StartedAt at the start of this segment
    /// (same clock as WorkoutTimeSeries.ElapsedSeconds).
    /// </summary>
    [Required]
    public int StartElapsedS { get; set; }

    /// <summary>
    /// Wall elapsed seconds from stored Workout.StartedAt at the end of this segment.
    /// Windows are [StartElapsedS, EndElapsedS); the last segment in a list is inclusive of its end.
    /// </summary>
    [Required]
    public int EndElapsedS { get; set; }

    /// <summary>
    /// Device-distance cursor (metres) at segment start when the source has a DistM stream.
    /// Null for Haversine-derived distance splits.
    /// </summary>
    [Column(TypeName = "double precision")]
    public double? StartDistanceM { get; set; }

    /// <summary>
    /// Time-weighted average heart rate for this split (bpm). Null when the series has no HR samples in the window.
    /// </summary>
    public byte? AvgHeartRateBpm { get; set; }

    // Navigation property
    [ForeignKey(nameof(WorkoutId))]
    public Workout Workout { get; set; } = null!;
}
