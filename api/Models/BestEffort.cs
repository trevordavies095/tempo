using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Tempo.Api.Models;

/// <summary>
/// Stores the best effort (fastest time) for each standard running distance.
/// Best efforts are calculated from any segment within any workout.
/// </summary>
public class BestEffort
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

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
    /// Fastest time in seconds for this distance
    /// </summary>
    [Required]
    public int TimeS { get; set; }

    /// <summary>
    /// Workout ID where this best effort was achieved
    /// </summary>
    [Required]
    public Guid WorkoutId { get; set; }

    /// <summary>
    /// Date of the workout where this best effort was achieved
    /// </summary>
    [Required]
    public DateTime WorkoutDate { get; set; }

    /// <summary>
    /// When this best effort was calculated/updated
    /// </summary>
    [Required]
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    [ForeignKey(nameof(WorkoutId))]
    public Workout Workout { get; set; } = null!;
}

