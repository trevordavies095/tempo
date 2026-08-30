using System.Globalization;
using System.Text.Json;
using Tempo.Api.Models;
using Tempo.Api.Utils;

namespace Tempo.Api.Services;

/// <summary>
/// Validates and maps a HealthKit import JSON document to DecodedWorkout + overlay.
/// Persist stays in WorkoutIntake; this is decode only.
/// </summary>
public class HealthKitWorkoutDecoder
{
    public const int SupportedSchemaVersion = 1;
    public const int MaxTrackPoints = 20_000;

    public sealed class DecodeResult
    {
        public DecodedWorkout? Decoded { get; init; }
        public WorkoutIntakeOverlay? Overlay { get; init; }
        public string? ErrorMessage { get; init; }
        public bool Success => Decoded != null && string.IsNullOrEmpty(ErrorMessage);
    }

    public DecodeResult Decode(HealthKitImportRequest? request)
    {
        if (request == null)
        {
            return Fail("Request body is required");
        }

        if (request.SchemaVersion != SupportedSchemaVersion)
        {
            return Fail($"Unsupported schemaVersion: expected {SupportedSchemaVersion}");
        }

        if (string.IsNullOrWhiteSpace(request.HealthKitUuid))
        {
            return Fail("healthKitUuid is required");
        }

        if (request.Summary == null)
        {
            return Fail("summary is required");
        }

        var summary = request.Summary;
        if (summary.StartedAt == default)
        {
            return Fail("summary.startedAt is required");
        }

        if (summary.DurationS <= 0)
        {
            return Fail("summary.durationS must be positive");
        }

        if (summary.DistanceM <= 0)
        {
            return Fail("summary.distanceM must be positive");
        }

        if (summary.IsIndoor)
        {
            return Fail("Indoor HealthKit workouts are not supported yet");
        }

        var points = request.TrackPoints ?? new List<HealthKitTrackPointDto>();
        if (points.Count > MaxTrackPoints)
        {
            return Fail($"trackPoints exceeds maximum of {MaxTrackPoints}");
        }

        var trackPoints = new List<TrackPoint>();
        foreach (var dto in points)
        {
            var mapped = MapTrackPoint(dto);
            if (mapped == null)
            {
                return Fail("Each trackPoint requires a valid timestamp (t)");
            }

            trackPoints.Add(mapped);
        }

        var positionedWithTime = trackPoints.Count(p => p.HasPosition && p.Time.HasValue);
        if (positionedWithTime < 2)
        {
            return Fail("Outdoor workouts require at least two trackPoints with lat, lon, and t");
        }

        var serializeOptions = new JsonSerializerOptions(JsonUtils.DefaultOptions)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var rawJson = JsonSerializer.Serialize(request, serializeOptions);
        var deviceName = request.SourceApp?.Name;

        return new DecodeResult
        {
            Decoded = new DecodedWorkout
            {
                StartedAt = summary.StartedAt,
                DurationS = summary.DurationS,
                DistanceM = summary.DistanceM,
                TrackPoints = trackPoints,
                SeriesPoints = null,
                Name = null,
                RawFileData = null,
                RawFileName = null,
                RawFileType = null
            },
            Overlay = new WorkoutIntakeOverlay
            {
                Source = "healthkit",
                RawHealthKitDataJson = rawJson,
                Device = deviceName,
                AvgHeartRateBpm = summary.AvgHeartRateBpm,
                MaxHeartRateBpm = summary.MaxHeartRateBpm,
                EnergyKcal = summary.EnergyKcal
            }
        };
    }

    private static TrackPoint? MapTrackPoint(HealthKitTrackPointDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.T) ||
            !DateTime.TryParse(dto.T, null, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time))
        {
            return null;
        }

        return new TrackPoint
        {
            Time = DateTime.SpecifyKind(time, DateTimeKind.Utc),
            Latitude = dto.Lat,
            Longitude = dto.Lon,
            Elevation = dto.Ele,
            HeartRateBpm = dto.Hr,
            CadenceRpm = dto.Cad,
            PowerWatts = dto.Pwr,
            DistanceM = dto.DistM
        };
    }

    private static DecodeResult Fail(string message) => new() { ErrorMessage = message };
}

/// <summary>
/// Versioned HealthKit import document from tempo-ios.
/// </summary>
public class HealthKitImportRequest
{
    public int SchemaVersion { get; set; }
    public string? HealthKitUuid { get; set; }
    public HealthKitSourceAppDto? SourceApp { get; set; }
    public HealthKitSummaryDto? Summary { get; set; }
    public List<HealthKitTrackPointDto>? TrackPoints { get; set; }
}

public class HealthKitSourceAppDto
{
    public string? Name { get; set; }
    public string? BundleId { get; set; }
}

public class HealthKitSummaryDto
{
    public DateTime StartedAt { get; set; }
    public int DurationS { get; set; }
    public double DistanceM { get; set; }
    public bool IsIndoor { get; set; }
    public ushort? EnergyKcal { get; set; }
    public byte? AvgHeartRateBpm { get; set; }
    public byte? MaxHeartRateBpm { get; set; }
}

public class HealthKitTrackPointDto
{
    public string? T { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    public double? Ele { get; set; }
    public byte? Hr { get; set; }
    public byte? Cad { get; set; }
    public ushort? Pwr { get; set; }
    public double? DistM { get; set; }
}
