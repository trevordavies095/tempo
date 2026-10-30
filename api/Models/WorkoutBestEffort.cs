using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Tempo.Api.Models;

/// <summary>
/// A workout's own fastest time for one standard running distance, from any segment within it.
/// One row per (workout, distance) the workout covers; all-time and windowed bests derive from these.
/// </summary>
public class WorkoutBestEffort
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Workout this effort was found in
    /// </summary>
    [Required]
    public Guid WorkoutId { get; set; }

    /// <summary>
    /// Distance name (e.g., "5K", "10K", "Marathon")
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Distance { get; set; } = string.Empty;

    /// <summary>
    /// Distance in meters
    /// </summary>
    [Required]
    [Column(TypeName = "double precision")]
    public double DistanceM { get; set; }

    /// <summary>
    /// Fastest time in seconds for this distance within the workout
    /// </summary>
    [Required]
    public int TimeS { get; set; }

    // Navigation property
    [ForeignKey(nameof(WorkoutId))]
    public Workout Workout { get; set; } = null!;
}
