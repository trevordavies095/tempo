namespace Tempo.Api.Models;

/// <summary>
/// In-memory sample on a Workout path. Not persisted as its own table.
/// </summary>
public class TrackPoint
{
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Elevation { get; set; }
    public DateTime? Time { get; set; }
    public byte? HeartRateBpm { get; set; }
    public byte? CadenceRpm { get; set; }
    public ushort? PowerWatts { get; set; }
    public sbyte? TemperatureC { get; set; }
    public double? SpeedMps { get; set; }
    public double? DistanceM { get; set; }
    public double? GradePercent { get; set; }
    public double? VerticalSpeedMps { get; set; }

    public bool HasPosition => Latitude.HasValue && Longitude.HasValue;
}
