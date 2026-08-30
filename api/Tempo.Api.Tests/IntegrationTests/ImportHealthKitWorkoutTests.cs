using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Api.Data;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

[Collection("Integration Tests")]
public class ImportHealthKitWorkoutTests : IClassFixture<TempoWebApplicationFactory>
{
    private readonly TempoWebApplicationFactory _factory;

    public ImportHealthKitWorkoutTests(TempoWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task EnsureCleanDatabaseAsync()
    {
        using var scope = _factory.Server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: true);
    }

    private static HealthKitImportRequest CreateOutdoorPayload(
        DateTime? startedAt = null,
        double distanceM = 5000,
        int durationS = 1800,
        string uuid = "A1B2C3D4-E5F6-7890-ABCD-EF1234567890")
    {
        var start = startedAt ?? new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        return new HealthKitImportRequest
        {
            SchemaVersion = 1,
            HealthKitUuid = uuid,
            SourceApp = new HealthKitSourceAppDto { Name = "Apple Watch", BundleId = "com.apple.health" },
            Summary = new HealthKitSummaryDto
            {
                StartedAt = start,
                DurationS = durationS,
                DistanceM = distanceM,
                IsIndoor = false,
                EnergyKcal = 420,
                AvgHeartRateBpm = 150,
                MaxHeartRateBpm = 175
            },
            TrackPoints = new List<HealthKitTrackPointDto>
            {
                new()
                {
                    T = start.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    Lat = 37.7749,
                    Lon = -122.4194,
                    Ele = 10,
                    Hr = 140,
                    Cad = 160,
                    Pwr = 250,
                    DistM = 0
                },
                new()
                {
                    T = start.AddMinutes(15).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    Lat = 37.7849,
                    Lon = -122.4094,
                    Ele = 25,
                    Hr = 155,
                    Cad = 165,
                    Pwr = 270,
                    DistM = distanceM / 2
                },
                new()
                {
                    T = start.AddSeconds(durationS).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    Lat = 37.7949,
                    Lon = -122.3994,
                    Ele = 40,
                    Hr = 160,
                    Cad = 168,
                    Pwr = 280,
                    DistM = distanceM
                }
            }
        };
    }

    [Fact]
    public async Task ImportHealthKit_ReturnsUnauthorized_WhenUnauthenticated()
    {
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/workouts/import/healthkit", CreateOutdoorPayload());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ImportHealthKit_ReturnsBadRequest_WhenSchemaVersionInvalid()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var payload = CreateOutdoorPayload();
        payload.SchemaVersion = 99;

        var response = await client.PostAsJsonAsync("/workouts/import/healthkit", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("schemaVersion");
    }

    [Fact]
    public async Task ImportHealthKit_ReturnsBadRequest_WhenIndoor()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var payload = CreateOutdoorPayload();
        payload.Summary!.IsIndoor = true;

        var response = await client.PostAsJsonAsync("/workouts/import/healthkit", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Indoor");
    }

    [Fact]
    public async Task ImportHealthKit_Outdoor_CreatesWorkoutWithRouteSplitsAndHr()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var payload = CreateOutdoorPayload();

        var response = await client.PostAsJsonAsync("/workouts/import/healthkit", payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("action").GetString().Should().Be("created");
        var workoutId = doc.RootElement.GetProperty("id").GetGuid();

        using var scope = _factory.Server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        var workout = await db.Workouts.SingleAsync(w => w.Id == workoutId);
        workout.Source.Should().Be("healthkit");
        workout.RawHealthKitData.Should().NotBeNullOrEmpty();
        workout.DistanceM.Should().Be(5000);
        workout.Calories.Should().Be(420);
        (await db.WorkoutRoutes.CountAsync(r => r.WorkoutId == workoutId)).Should().Be(1);
        (await db.WorkoutSplits.CountAsync(s => s.WorkoutId == workoutId)).Should().BeGreaterThan(0);
        (await db.WorkoutTimeSeries.CountAsync(ts => ts.WorkoutId == workoutId && ts.HeartRateBpm != null))
            .Should().BeGreaterThan(0);

        var details = await client.GetAsync($"/workouts/{workoutId}");
        details.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailsJson = await details.Content.ReadAsStringAsync();
        detailsJson.Should().Contain("rawHealthKitData");
        detailsJson.Should().Contain("healthKitUuid");
    }

    [Fact]
    public async Task ImportHealthKit_Skipped_WhenMatchingGpxAlreadyImported()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        var start = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var gpx = CreateGpxMatching(start, durationMinutes: 30, distanceKmApprox: 5.0);
        var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(gpx));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/gpx+xml");
        fileContent.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("form-data")
        {
            Name = "file",
            FileName = "match.gpx"
        };
        formData.Add(fileContent);

        var gpxResponse = await client.PostAsync("/workouts/import", formData);
        gpxResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var gpxDoc = JsonDocument.Parse(await gpxResponse.Content.ReadAsStringAsync());
        var gpxId = gpxDoc.RootElement.GetProperty("id").GetGuid();
        var distanceM = gpxDoc.RootElement.GetProperty("distanceM").GetDouble();
        var durationS = gpxDoc.RootElement.GetProperty("durationS").GetInt32();

        var payload = CreateOutdoorPayload(startedAt: start, distanceM: distanceM, durationS: durationS);
        var hkResponse = await client.PostAsJsonAsync("/workouts/import/healthkit", payload);

        hkResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var hkDoc = JsonDocument.Parse(await hkResponse.Content.ReadAsStringAsync());
        hkDoc.RootElement.GetProperty("action").GetString().Should().Be("skipped");
        hkDoc.RootElement.GetProperty("id").GetGuid().Should().Be(gpxId);

        using var scope = _factory.Server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        (await db.Workouts.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ImportHealthKit_SecondPost_IsSkipped()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var payload = CreateOutdoorPayload();

        var first = await client.PostAsJsonAsync("/workouts/import/healthkit", payload);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        using var firstDoc = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        firstDoc.RootElement.GetProperty("action").GetString().Should().Be("created");
        var id = firstDoc.RootElement.GetProperty("id").GetGuid();

        var second = await client.PostAsJsonAsync("/workouts/import/healthkit", payload);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        using var secondDoc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        secondDoc.RootElement.GetProperty("action").GetString().Should().Be("skipped");
        secondDoc.RootElement.GetProperty("id").GetGuid().Should().Be(id);

        using var scope = _factory.Server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        (await db.Workouts.CountAsync()).Should().Be(1);
    }

    /// <summary>
    /// GPX whose parsed start/duration/distance can be matched for HealthKit duplicate tests.
    /// Points spaced for ~5km over 30 minutes (same geometry approach as ImportWorkoutTests).
    /// </summary>
    private static string CreateGpxMatching(DateTime start, int durationMinutes, double distanceKmApprox)
    {
        var startLat = 37.7749;
        var startLon = -122.4194;
        var degreeIncrement = distanceKmApprox / 141.0;
        var numPoints = 20;
        var points = new List<string>();
        for (var i = 0; i < numPoints; i++)
        {
            var progress = (double)i / (numPoints - 1);
            var lat = startLat + (progress * degreeIncrement);
            var lon = startLon + (progress * degreeIncrement);
            var ele = 10.0 + (progress * 50.0);
            var time = start.AddMinutes(progress * durationMinutes);
            points.Add($@"      <trkpt lat=""{lat:F6}"" lon=""{lon:F6}"">
        <ele>{ele:F1}</ele>
        <time>{time:yyyy-MM-ddTHH:mm:ss}Z</time>
      </trkpt>");
        }

        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<gpx version=""1.1"" creator=""Tempo Test"" xmlns=""http://www.topografix.com/GPX/1/1"">
  <trk>
    <name>Test Run</name>
    <trkseg>
{string.Join("\n", points)}
    </trkseg>
  </trk>
</gpx>";
    }
}
