using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Tempo.Api.Models;

public class Workout
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public DateTime StartedAt { get; set; }

    [Required]
    public int DurationS { get; set; }

    [Required]
    [Column(TypeName = "double precision")]
    public double DistanceM { get; set; }

    [Required]
    public int AvgPaceS { get; set; }

    [Column(TypeName = "double precision")]
    public double? ElevGainM { get; set; }

    [MaxLength(50)]
    public string? RunType { get; set; }

    [Column(TypeName = "text")]
    public string? Notes { get; set; }

    [MaxLength(50)]
    public string? Source { get; set; }

    [Column(TypeName = "jsonb")]
    public string? Weather { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public WorkoutRoute? Route { get; set; }
    public ICollection<WorkoutSplit> Splits { get; set; } = new List<WorkoutSplit>();
    public ICollection<WorkoutMedia> Media { get; set; } = new List<WorkoutMedia>();
}

