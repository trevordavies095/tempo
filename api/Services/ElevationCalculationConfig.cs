namespace Tempo.Api.Services;

public class ElevationCalculationConfig
{
    public double NoiseThresholdMeters { get; set; } = 2.0;
    public double MinDistanceMeters { get; set; } = 10.0;
}

