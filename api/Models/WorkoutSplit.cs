using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Tempo.Api.Models;

public class WorkoutSplit
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid WorkoutId { get; set; }

    [Required]
    public int Idx { get; set; }

    [Required]
    [Column(TypeName = "double precision")]
    public double DistanceM { get; set; }

    [Required]
    public int DurationS { get; set; }

    [Required]
    public int PaceS { get; set; }

    // Navigation property
    [ForeignKey(nameof(WorkoutId))]
    public Workout Workout { get; set; } = null!;
}

