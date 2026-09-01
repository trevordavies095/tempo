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

    /// <summary>
    /// Douglas-Peucker preview of <see cref="RouteGeoJson"/> (≤ 100 points).
    /// Null means not yet computed; <c>[]</c> is a sentinel for empty or unparseable source geometry.
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string? PreviewGeoJson { get; set; }

    // Navigation property
    [ForeignKey(nameof(WorkoutId))]
    public Workout Workout { get; set; } = null!;
}

