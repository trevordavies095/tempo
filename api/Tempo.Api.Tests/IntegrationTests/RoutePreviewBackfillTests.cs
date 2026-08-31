using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

[Collection("Integration Tests")]
public class RoutePreviewBackfillTests : IClassFixture<TempoWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TempoWebApplicationFactory _factory;

    public RoutePreviewBackfillTests(TempoWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task EnsureCleanDatabaseAsync()
    {
        using var scope = _factory.Server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: true);
    }

    [Fact]
    public async Task ExportRestoreBackfill_ListReturnsPreviews_ForRestoredGpsWorkouts()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var coordinates = CreateWavyCoordinates(180);
        Guid workoutId;

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedUserSettingsAsync(db);
            var workout = await TestDataSeeder.SeedWorkoutAsync(db, name: "GPS Run", distanceM: 8000, durationS: 2400);
            await TestDataSeeder.SeedWorkoutWithRouteAsync(db, workout, coordinates);
            workoutId = workout.Id;
        }

        var exportZip = await ImportTestHelper.CreateExportZipWithDataAsync(client);
        await EnsureCleanDatabaseAsync();

        exportZip.Position = 0;
        var formContent = new MultipartFormDataContent();
        var streamContent = new StreamContent(exportZip);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        formContent.Add(streamContent, "file", "export.zip");

        var importResponse = await client.PostAsync("/workouts/import/export", formContent);
        importResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var started = await importResponse.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);
        started.Should().NotBeNull();
        var job = await PollUntilTerminalAsync(client, started!.Id);
        job.Status.Should().Be(ImportJobStatuses.Completed);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var restored = await db.WorkoutRoutes.SingleAsync(r => r.WorkoutId == workoutId);
            restored.PreviewGeoJson.Should().BeNull();
            CountLineStringPoints(restored.RouteGeoJson).Should().Be(180);

            var backfill = scope.ServiceProvider.GetRequiredService<RoutePreviewBackfillService>();
            var processed = await backfill.RunAsync();
            processed.Should().Be(1);

            restored = await db.WorkoutRoutes.SingleAsync(r => r.WorkoutId == workoutId);
            restored.PreviewGeoJson.Should().NotBeNullOrEmpty();
            CountLineStringPoints(restored.PreviewGeoJson!).Should().BeLessThanOrEqualTo(TrackGeometry.RoutePreviewMaxPoints);
            CountLineStringPoints(restored.PreviewGeoJson!).Should().BeLessThan(180);
        }

        var listResponse = await client.GetAsync("/workouts");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var item = payload.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("id").GetGuid().Should().Be(workoutId);
        item.GetProperty("hasRoute").GetBoolean().Should().BeTrue();
        var listCoords = item.GetProperty("route").GetProperty("coordinates").GetArrayLength();
        listCoords.Should().BeLessThanOrEqualTo(TrackGeometry.RoutePreviewMaxPoints);
        listCoords.Should().BeLessThan(180);
    }

    [Fact]
    public async Task ListWorkouts_PostBackfill_IsAtLeast90PercentSmallerThanFullRouteControl()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        const int workoutCount = 25;
        const int pointCount = 3000;

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            for (var i = 0; i < workoutCount; i++)
            {
                var workout = await TestDataSeeder.SeedWorkoutAsync(
                    db,
                    startedAt: DateTime.UtcNow.AddHours(-(i + 1)),
                    name: $"Heavy route {i}",
                    distanceM: 10000,
                    durationS: 3600);
                await TestDataSeeder.SeedWorkoutWithRouteAsync(db, workout, CreateWavyCoordinates(pointCount));
            }
        }

        var controlResponse = await client.GetAsync("/workouts?pageSize=25");
        controlResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var controlBytes = await controlResponse.Content.ReadAsByteArrayAsync();
        var controlPayload = JsonSerializer.Deserialize<JsonElement>(controlBytes);
        controlPayload.GetProperty("items").GetArrayLength().Should().Be(workoutCount);
        controlPayload.GetProperty("items")[0].GetProperty("route").GetProperty("coordinates")
            .GetArrayLength().Should().Be(pointCount);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var backfill = scope.ServiceProvider.GetRequiredService<RoutePreviewBackfillService>();
            (await backfill.RunAsync()).Should().Be(workoutCount);
        }

        var previewResponse = await client.GetAsync("/workouts?pageSize=25");
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewBytes = await previewResponse.Content.ReadAsByteArrayAsync();
        var previewPayload = JsonSerializer.Deserialize<JsonElement>(previewBytes);
        previewPayload.GetProperty("items").GetArrayLength().Should().Be(workoutCount);
        previewPayload.GetProperty("items")[0].GetProperty("route").GetProperty("coordinates")
            .GetArrayLength().Should().BeLessThanOrEqualTo(TrackGeometry.RoutePreviewMaxPoints);

        controlBytes.Length.Should().BeGreaterThan(0);
        var reduction = 1.0 - ((double)previewBytes.Length / controlBytes.Length);
        reduction.Should().BeGreaterThanOrEqualTo(
            0.90,
            $"post-backfill list ({previewBytes.Length} bytes) should be ≥ 90% smaller than full-route control ({controlBytes.Length} bytes)");
    }

    private static List<double[]> CreateWavyCoordinates(int count)
    {
        var coordinates = new List<double[]>(count);
        for (var i = 0; i < count; i++)
        {
            coordinates.Add(new[]
            {
                -122.4194 + i * 0.0008,
                37.7749 + Math.Sin(i / 2.5) * 0.012
            });
        }

        return coordinates;
    }

    private static int CountLineStringPoints(string geoJson)
    {
        using var doc = JsonDocument.Parse(geoJson);
        return doc.RootElement.GetProperty("coordinates").GetArrayLength();
    }

    private static async Task<ImportJobDocument> PollUntilTerminalAsync(HttpClient client, Guid id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        ImportJobDocument? job = null;
        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/workouts/import/jobs/{id}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            job = await response.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);
            job.Should().NotBeNull();
            if (job!.Status is ImportJobStatuses.Completed or ImportJobStatuses.Failed)
            {
                return job;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Import job {id} did not finish. Last status: {job?.Status}");
    }
}
