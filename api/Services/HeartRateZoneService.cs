using Tempo.Api.Models;

namespace Tempo.Api.Services;

public class HeartRateZoneService
{
    // Standard zone percentages
    private static readonly (double MinPercent, double MaxPercent)[] ZonePercentages = new[]
    {
        (0.50, 0.60), // Zone 1: 50-60%
        (0.60, 0.70), // Zone 2: 60-70%
        (0.70, 0.80), // Zone 3: 70-80%
        (0.80, 0.90), // Zone 4: 80-90%
        (0.90, 1.00)  // Zone 5: 90-100%
    };

    /// <summary>
    /// Calculate heart rate zones using the 220-age formula
    /// </summary>
    public List<HeartRateZone> CalculateZonesFromAge(int age)
    {
        if (age < 1 || age > 120)
        {
            throw new ArgumentException("Age must be between 1 and 120", nameof(age));
        }

        int maxHeartRate = 220 - age;
        return CalculateZonesFromMaxHeartRate(maxHeartRate);
    }

    /// <summary>
    /// Calculate heart rate zones using the Karvonen formula (Heart Rate Reserve)
    /// </summary>
    public List<HeartRateZone> CalculateZonesFromKarvonen(int maxHeartRate, int restingHeartRate)
    {
        if (maxHeartRate < 60 || maxHeartRate > 250)
        {
            throw new ArgumentException("Max heart rate must be between 60 and 250 BPM", nameof(maxHeartRate));
        }

        if (restingHeartRate < 30 || restingHeartRate > 120)
        {
            throw new ArgumentException("Resting heart rate must be between 30 and 120 BPM", nameof(restingHeartRate));
        }

        if (maxHeartRate <= restingHeartRate)
        {
            throw new ArgumentException("Max heart rate must be greater than resting heart rate");
        }

        int heartRateReserve = maxHeartRate - restingHeartRate;
        var zones = new List<HeartRateZone>();

        foreach (var (minPercent, maxPercent) in ZonePercentages)
        {
            int minBpm = (int)Math.Round((heartRateReserve * minPercent) + restingHeartRate);
            int maxBpm = (int)Math.Round((heartRateReserve * maxPercent) + restingHeartRate);
            zones.Add(new HeartRateZone { MinBpm = minBpm, MaxBpm = maxBpm });
        }

        return zones;
    }

    /// <summary>
    /// Calculate zones as simple percentages of max heart rate (for 220-age method)
    /// </summary>
    private List<HeartRateZone> CalculateZonesFromMaxHeartRate(int maxHeartRate)
    {
        var zones = new List<HeartRateZone>();

        foreach (var (minPercent, maxPercent) in ZonePercentages)
        {
            int minBpm = (int)Math.Round(maxHeartRate * minPercent);
            int maxBpm = (int)Math.Round(maxHeartRate * maxPercent);
            zones.Add(new HeartRateZone { MinBpm = minBpm, MaxBpm = maxBpm });
        }

        return zones;
    }

    /// <summary>
    /// Validate custom zone boundaries
    /// </summary>
    public (bool IsValid, string? ErrorMessage) ValidateCustomZones(List<HeartRateZone> zones)
    {
        if (zones == null || zones.Count != 5)
        {
            return (false, "Exactly 5 zones are required");
        }

        // Check for null zones
        if (zones.Any(z => z == null))
        {
            return (false, "All zones must be defined");
        }

        // Check that min < max for each zone
        for (int i = 0; i < zones.Count; i++)
        {
            if (zones[i].MinBpm >= zones[i].MaxBpm)
            {
                return (false, $"Zone {i + 1}: Minimum BPM must be less than maximum BPM");
            }

            if (zones[i].MinBpm < 30 || zones[i].MaxBpm > 250)
            {
                return (false, $"Zone {i + 1}: BPM values must be between 30 and 250");
            }
        }

        // Check that zones are in ascending order and don't overlap
        for (int i = 0; i < zones.Count - 1; i++)
        {
            if (zones[i].MaxBpm > zones[i + 1].MinBpm)
            {
                return (false, $"Zone {i + 1} and Zone {i + 2} overlap or are not in ascending order");
            }
        }

        // Check that zones are contiguous (no gaps)
        for (int i = 0; i < zones.Count - 1; i++)
        {
            if (zones[i].MaxBpm < zones[i + 1].MinBpm)
            {
                return (false, $"Zone {i + 1} and Zone {i + 2} have a gap between them");
            }
        }

        return (true, null);
    }

    /// <summary>
    /// Apply zones to UserSettings model
    /// </summary>
    public void ApplyZonesToUserSettings(UserSettings settings, List<HeartRateZone> zones)
    {
        if (zones == null || zones.Count != 5)
        {
            throw new ArgumentException("Exactly 5 zones are required", nameof(zones));
        }

        settings.Zone1MinBpm = zones[0].MinBpm;
        settings.Zone1MaxBpm = zones[0].MaxBpm;
        settings.Zone2MinBpm = zones[1].MinBpm;
        settings.Zone2MaxBpm = zones[1].MaxBpm;
        settings.Zone3MinBpm = zones[2].MinBpm;
        settings.Zone3MaxBpm = zones[2].MaxBpm;
        settings.Zone4MinBpm = zones[3].MinBpm;
        settings.Zone4MaxBpm = zones[3].MaxBpm;
        settings.Zone5MinBpm = zones[4].MinBpm;
        settings.Zone5MaxBpm = zones[4].MaxBpm;
    }

    /// <summary>
    /// Get zones from UserSettings model
    /// </summary>
    public List<HeartRateZone> GetZonesFromUserSettings(UserSettings settings)
    {
        return new List<HeartRateZone>
        {
            new HeartRateZone { MinBpm = settings.Zone1MinBpm, MaxBpm = settings.Zone1MaxBpm },
            new HeartRateZone { MinBpm = settings.Zone2MinBpm, MaxBpm = settings.Zone2MaxBpm },
            new HeartRateZone { MinBpm = settings.Zone3MinBpm, MaxBpm = settings.Zone3MaxBpm },
            new HeartRateZone { MinBpm = settings.Zone4MinBpm, MaxBpm = settings.Zone4MaxBpm },
            new HeartRateZone { MinBpm = settings.Zone5MinBpm, MaxBpm = settings.Zone5MaxBpm }
        };
    }
}

