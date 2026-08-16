using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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
public class ImportJobTests : IClassFixture<TempoWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TempoWebApplicationFactory _factory;

    public ImportJobTests(TempoWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task EnsureCleanDatabaseAsync()
    {
        using var scope = _factory.Server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: true);
        await TestDataSeeder.SeedUserSettingsAsync(db);
    }

    [Fact]
    public async Task BulkImport_AcceptedJob_CompletesWithWorkoutAndDeletesArchive()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        var response = await client.PostAsync("/workouts/import/bulk", CreateZipForm(includeCsv: true));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var started = await response.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);
        started.Should().NotBeNull();
        started!.Id.Should().NotBeEmpty();
        started.Status.Should().BeOneOf(ImportJobStatuses.Queued, ImportJobStatuses.Running, ImportJobStatuses.Completed);

        var job = await PollUntilTerminalAsync(client, started.Id);
        job.Status.Should().Be(ImportJobStatuses.Completed);
        job.Successful.Should().Be(1);
        job.Total.Should().Be(1);
        job.ErrorMessage.Should().BeNull();

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var media = scope.ServiceProvider.GetRequiredService<MediaStorageConfig>();
            (await db.Workouts.CountAsync()).Should().Be(1);
            var row = await db.ImportJobs.FindAsync(started.Id);
            row.Should().NotBeNull();
            row!.ArchivePath.Should().BeNull();
            Directory.Exists(Path.Combine(media.RootPath, "imports", started.Id.ToString())).Should().BeFalse();
        }
    }

    [Fact]
    public async Task BulkImport_MissingActivitiesCsv_FailsJobWithMessage()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        var response = await client.PostAsync("/workouts/import/bulk", CreateZipForm(includeCsv: false));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var started = await response.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);
        started.Should().NotBeNull();

        var job = await PollUntilTerminalAsync(client, started!.Id);
        job.Status.Should().Be(ImportJobStatuses.Failed);
        job.ErrorMessage.Should().Contain("activities.csv");
        job.ErrorMessage.Should().NotContain("No file uploaded");

        using var scope = _factory.Server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        (await db.Workouts.CountAsync()).Should().Be(0);
        (await db.ImportJobs.FindAsync(started.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task ImportJobRoutes_RequireAuth()
    {
        var client = _factory.CreateClient();
        (await client.GetAsync($"/workouts/import/jobs/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsync("/workouts/import/bulk", CreateZipForm(includeCsv: true))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SingleFileImport_StillReturns200()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var gpx = Encoding.UTF8.GetBytes(GpxContents);
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(gpx);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/gpx+xml");
        file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "file",
            FileName = "test.gpx"
        };
        form.Add(file);

        var response = await client.PostAsync("/workouts/import", form);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<ImportJobDocument> PollUntilTerminalAsync(HttpClient client, Guid id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
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

    private static MultipartFormDataContent CreateZipForm(bool includeCsv)
    {
        var zipBytes = CreateZipBytes(includeCsv);
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(zipBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "file",
            FileName = "export.zip"
        };
        form.Add(file);
        return form;
    }

    private static byte[] CreateZipBytes(bool includeCsv)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (includeCsv)
            {
                var csv = archive.CreateEntry("activities.csv");
                using var writer = new StreamWriter(csv.Open(), Encoding.UTF8);
                writer.WriteLine("Activity ID,Activity Date,Activity Name,Activity Type,Activity Description,Filename,Activity Private Note,Media");
                writer.WriteLine("1,2024-01-15,Morning Run,Run,,activities/morning.gpx,,");
            }

            var gpx = archive.CreateEntry("activities/morning.gpx");
            using (var writer = new StreamWriter(gpx.Open(), Encoding.UTF8))
            {
                writer.Write(GpxContents);
            }
        }

        return stream.ToArray();
    }

    private const string GpxContents = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<gpx version=""1.1"" xmlns=""http://www.topografix.com/GPX/1/1"">
  <trk>
    <name>Morning Run</name>
    <trkseg>
      <trkpt lat=""37.7749"" lon=""-122.4194"">
        <ele>10</ele>
        <time>2024-01-15T10:00:00Z</time>
      </trkpt>
      <trkpt lat=""37.7849"" lon=""-122.4094"">
        <ele>20</ele>
        <time>2024-01-15T10:10:00Z</time>
      </trkpt>
      <trkpt lat=""37.7949"" lon=""-122.3994"">
        <ele>30</ele>
        <time>2024-01-15T10:20:00Z</time>
      </trkpt>
    </trkseg>
  </trk>
</gpx>";
}
