using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Tempo.Api.Models;

public class WorkoutRoute
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid WorkoutId { get; set; }

    [Required]
    [Column(TypeName = "jsonb")]
    public string RouteGeoJson { get; set; } = string.Empty;

    // Navigation property
    [ForeignKey(nameof(WorkoutId))]
    public Workout Workout { get; set; } = null!;
}

