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
        (await client.GetAsync("/workouts/import/jobs/current")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/workouts/import/jobs", new { filename = "export.zip", byteSize = 10 })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsync("/workouts/import/bulk", CreateZipForm(includeCsv: true))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.DeleteAsync($"/workouts/import/jobs/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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

    [Fact]
    public async Task ChunkedImport_CompletesWithWorkout()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var zipBytes = CreateZipBytes(includeCsv: true);

        var createdResponse = await client.PostAsJsonAsync("/workouts/import/jobs", new
        {
            kind = ImportJobKinds.StravaBulk,
            filename = "export.zip",
            byteSize = zipBytes.Length,
            unitPreference = "metric"
        });
        createdResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createdResponse.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);
        created.Should().NotBeNull();
        created!.Status.Should().Be(ImportJobStatuses.Receiving);

        await PutAllChunksAsync(client, created.Id, zipBytes);

        var complete = await client.PostAsync($"/workouts/import/jobs/{created.Id}/complete", null);
        complete.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var queued = await complete.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);
        queued!.Status.Should().BeOneOf(ImportJobStatuses.Queued, ImportJobStatuses.Running, ImportJobStatuses.Completed);

        var job = await PollUntilTerminalAsync(client, created.Id);
        job.Status.Should().Be(ImportJobStatuses.Completed);
        job.Successful.Should().Be(1);
        job.Statistics.Should().BeNull();
        job.Warnings.Should().BeNull();
        job.ErrorMessages.Should().BeNull();

        using var scope = _factory.Server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        (await db.Workouts.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ChunkedImport_CompleteWithWrongSize_DoesNotEnqueue()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var zipBytes = CreateZipBytes(includeCsv: true);

        var createdResponse = await client.PostAsJsonAsync("/workouts/import/jobs", new
        {
            kind = ImportJobKinds.StravaBulk,
            filename = "export.zip",
            byteSize = zipBytes.Length
        });
        var created = await createdResponse.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);
        created.Should().NotBeNull();

        var truncated = zipBytes.AsSpan(0, Math.Max(1, zipBytes.Length / 2)).ToArray();
        await PutChunkAsync(client, created!.Id, 0, 1, truncated);

        var complete = await client.PostAsync($"/workouts/import/jobs/{created.Id}/complete", null);
        complete.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var still = await client.GetAsync($"/workouts/import/jobs/{created.Id}");
        var job = await still.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);
        job!.Status.Should().Be(ImportJobStatuses.Receiving);

        await Task.Delay(200);
        using var scope = _factory.Server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        (await db.Workouts.CountAsync()).Should().Be(0);
        (await db.ImportJobs.FindAsync(created.Id))!.Status.Should().Be(ImportJobStatuses.Receiving);
    }

    [Fact]
    public async Task CreateWhileActive_Returns409WithId_AndCurrentMatches()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var zipBytes = CreateZipBytes(includeCsv: true);

        var first = await client.PostAsJsonAsync("/workouts/import/jobs", new
        {
            kind = ImportJobKinds.StravaBulk,
            filename = "export.zip",
            byteSize = zipBytes.Length
        });
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await first.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);

        var second = await client.PostAsJsonAsync("/workouts/import/jobs", new
        {
            kind = ImportJobKinds.StravaBulk,
            filename = "other.zip",
            byteSize = zipBytes.Length
        });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var conflict = await second.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);
        conflict!.Id.Should().Be(created!.Id);

        var adapter = await client.PostAsync("/workouts/import/bulk", CreateZipForm(includeCsv: true));
        adapter.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var current = await client.GetAsync("/workouts/import/jobs/current");
        current.StatusCode.Should().Be(HttpStatusCode.OK);
        var currentJob = await current.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);
        currentJob!.Id.Should().Be(created.Id);

        (await client.DeleteAsync($"/workouts/import/jobs/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/workouts/import/jobs/current")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetCurrent_NoActiveJob_Returns204()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        (await client.GetAsync("/workouts/import/jobs/current")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CancelRunning_KeepsImportedWorkouts_AndAllowsRetry()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        var response = await client.PostAsync("/workouts/import/bulk", CreateZipForm(includeCsv: true, activityCount: 8));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var started = await response.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);

        var deadline = DateTime.UtcNow.AddSeconds(20);
        ImportJobDocument? snapshot = null;
        while (DateTime.UtcNow < deadline)
        {
            var poll = await client.GetAsync($"/workouts/import/jobs/{started!.Id}");
            snapshot = await poll.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);
            if (snapshot!.Processed >= 1 && snapshot.Status == ImportJobStatuses.Running)
            {
                break;
            }
            if (snapshot.Status is ImportJobStatuses.Completed or ImportJobStatuses.Failed)
            {
                break;
            }
            await Task.Delay(20);
        }

        snapshot.Should().NotBeNull();
        snapshot!.Processed.Should().BeGreaterThanOrEqualTo(1);
        if (snapshot.Status is ImportJobStatuses.Queued or ImportJobStatuses.Running or ImportJobStatuses.Receiving)
        {
            var cancel = await client.DeleteAsync($"/workouts/import/jobs/{started!.Id}");
            cancel.StatusCode.Should().Be(HttpStatusCode.OK);
            var finished = await PollUntilTerminalAsync(client, started.Id);
            finished.Status.Should().Be(ImportJobStatuses.Failed);
            finished.ErrorMessage.Should().Be(ImportJobErrorMessages.Cancelled);
        }

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var media = scope.ServiceProvider.GetRequiredService<MediaStorageConfig>();
            (await db.Workouts.CountAsync()).Should().BeGreaterThanOrEqualTo(1);
            Directory.Exists(Path.Combine(media.RootPath, "imports", started!.Id.ToString())).Should().BeFalse();
        }

        var retry = await client.PostAsync("/workouts/import/bulk", CreateZipForm(includeCsv: true, activityCount: 1));
        retry.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var retryJob = await retry.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);
        await PollUntilTerminalAsync(client, retryJob!.Id);
    }

    [Fact]
    public async Task StaleReceiving_IsReplacedOnCreate()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var zipBytes = CreateZipBytes(includeCsv: true);

        var first = await client.PostAsJsonAsync("/workouts/import/jobs", new
        {
            kind = ImportJobKinds.StravaBulk,
            filename = "old.zip",
            byteSize = zipBytes.Length
        });
        var stale = await first.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var row = await db.ImportJobs.FindAsync(stale!.Id);
            row!.LastChunkAt = DateTime.UtcNow.AddMinutes(-16);
            row.CreatedAt = DateTime.UtcNow.AddMinutes(-16);
            await db.SaveChangesAsync();
        }

        var second = await client.PostAsJsonAsync("/workouts/import/jobs", new
        {
            kind = ImportJobKinds.StravaBulk,
            filename = "new.zip",
            byteSize = zipBytes.Length
        });
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        var replacement = await second.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);
        replacement!.Id.Should().NotBe(stale!.Id);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var old = await db.ImportJobs.FindAsync(stale.Id);
            old!.Status.Should().Be(ImportJobStatuses.Failed);
        }

        (await client.DeleteAsync($"/workouts/import/jobs/{replacement.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RunningJob_IsNotReplacedByStaleReceivingRule()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var zipBytes = CreateZipBytes(includeCsv: true);

        var first = await client.PostAsJsonAsync("/workouts/import/jobs", new
        {
            kind = ImportJobKinds.StravaBulk,
            filename = "export.zip",
            byteSize = zipBytes.Length
        });
        var created = await first.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var row = await db.ImportJobs.FindAsync(created!.Id);
            row!.Status = ImportJobStatuses.Running;
            row.LastChunkAt = DateTime.UtcNow.AddMinutes(-30);
            row.CreatedAt = DateTime.UtcNow.AddMinutes(-30);
            await db.SaveChangesAsync();
        }

        var second = await client.PostAsJsonAsync("/workouts/import/jobs", new
        {
            kind = ImportJobKinds.StravaBulk,
            filename = "new.zip",
            byteSize = zipBytes.Length
        });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var importJobs = scope.ServiceProvider.GetRequiredService<ImportJobService>();
            await importJobs.InterruptIncompleteJobsAsync();
        }
    }

    [Fact]
    public async Task StartupInterrupt_FailsQueuedAndRunning_AndAllowsNewJob()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        Guid queuedId;
        Guid runningId;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var media = scope.ServiceProvider.GetRequiredService<MediaStorageConfig>();

            var queued = new ImportJob
            {
                Kind = ImportJobKinds.StravaBulk,
                Status = ImportJobStatuses.Queued,
                Filename = "queued.zip",
                ByteSize = 10,
                BytesReceived = 10
            };
            var running = new ImportJob
            {
                Kind = ImportJobKinds.StravaBulk,
                Status = ImportJobStatuses.Running,
                Filename = "running.zip",
                ByteSize = 10,
                BytesReceived = 10,
                StartedAt = DateTime.UtcNow
            };
            db.ImportJobs.AddRange(queued, running);
            await db.SaveChangesAsync();
            queuedId = queued.Id;
            runningId = running.Id;

            foreach (var id in new[] { queuedId, runningId })
            {
                var dir = Path.Combine(media.RootPath, "imports", id.ToString());
                Directory.CreateDirectory(dir);
                await File.WriteAllBytesAsync(Path.Combine(dir, "archive.zip"), [1, 2, 3]);
                var row = await db.ImportJobs.FindAsync(id);
                row!.ArchivePath = Path.Combine(dir, "archive.zip");
            }
            await db.SaveChangesAsync();

            var importJobs = scope.ServiceProvider.GetRequiredService<ImportJobService>();
            await importJobs.InterruptIncompleteJobsAsync();
        }

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var media = scope.ServiceProvider.GetRequiredService<MediaStorageConfig>();
            var queued = await db.ImportJobs.FindAsync(queuedId);
            var running = await db.ImportJobs.FindAsync(runningId);
            queued!.Status.Should().Be(ImportJobStatuses.Failed);
            queued.ErrorMessage.Should().Be(ImportJobErrorMessages.Interrupted);
            running!.Status.Should().Be(ImportJobStatuses.Failed);
            running.ErrorMessage.Should().Be(ImportJobErrorMessages.Interrupted);
            Directory.Exists(Path.Combine(media.RootPath, "imports", queuedId.ToString())).Should().BeFalse();
            Directory.Exists(Path.Combine(media.RootPath, "imports", runningId.ToString())).Should().BeFalse();
        }

        var next = await client.PostAsJsonAsync("/workouts/import/jobs", new
        {
            kind = ImportJobKinds.StravaBulk,
            filename = "export.zip",
            byteSize = 12
        });
        next.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await next.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);
        (await client.DeleteAsync($"/workouts/import/jobs/{created!.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateImportJob_RequiresKind()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        var missing = await client.PostAsJsonAsync("/workouts/import/jobs", new
        {
            filename = "export.zip",
            byteSize = 10
        });
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var unknown = await client.PostAsJsonAsync("/workouts/import/jobs", new
        {
            kind = "other_kind",
            filename = "export.zip",
            byteSize = 10
        });
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateTempoExport_RejectsUnitPreference()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        var response = await client.PostAsJsonAsync("/workouts/import/jobs", new
        {
            kind = ImportJobKinds.TempoExport,
            filename = "export.zip",
            byteSize = 10,
            unitPreference = "metric"
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TempoExport_ChunkedImport_CompletesWithStatistics()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedUserSettingsAsync(db);
            var shoe = await TestDataSeeder.SeedShoeAsync(db, "Nike", "Pegasus");
            await TestDataSeeder.SeedWorkoutCompleteAsync(db, shoeId: shoe.Id, distanceM: 5000, durationS: 1800);
        }

        var exportZip = await ImportTestHelper.CreateExportZipWithDataAsync(client);
        var zipBytes = exportZip.ToArray();
        await EnsureCleanDatabaseAsync();

        var createdResponse = await client.PostAsJsonAsync("/workouts/import/jobs", new
        {
            kind = ImportJobKinds.TempoExport,
            filename = "tempo-export.zip",
            byteSize = zipBytes.Length
        });
        createdResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createdResponse.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);
        created!.Kind.Should().Be(ImportJobKinds.TempoExport);

        await PutAllChunksAsync(client, created.Id, zipBytes);
        var complete = await client.PostAsync($"/workouts/import/jobs/{created.Id}/complete", null);
        complete.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var job = await PollUntilTerminalAsync(client, created.Id);
        job.Status.Should().Be(ImportJobStatuses.Completed);
        job.Statistics.Should().NotBeNull();
        job.Updated.Should().Be(0);
        job.ErrorDetails.Should().BeEmpty();
        job.Successful.Should().BeGreaterThan(0);
        job.Total.Should().BeGreaterThan(0);
        job.Processed.Should().Be(job.Successful + job.Skipped + job.Errors);

        using var verifyScope = _factory.Server.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TempoDbContext>();
        var media = verifyScope.ServiceProvider.GetRequiredService<MediaStorageConfig>();
        (await verifyDb.Workouts.CountAsync()).Should().BeGreaterThan(0);
        var row = await verifyDb.ImportJobs.FindAsync(created.Id);
        row!.ArchivePath.Should().BeNull();
        Directory.Exists(Path.Combine(media.RootPath, "imports", created.Id.ToString())).Should().BeFalse();
    }

    [Fact]
    public async Task TempoExport_AdapterPost_Returns202AndCompletes()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedUserSettingsAsync(db);
            var shoe = await TestDataSeeder.SeedShoeAsync(db, "Brooks", "Ghost");
            await TestDataSeeder.SeedWorkoutCompleteAsync(db, shoeId: shoe.Id, distanceM: 10000, durationS: 3600);
        }

        var exportZip = await ImportTestHelper.CreateExportZipWithDataAsync(client);
        await EnsureCleanDatabaseAsync();

        exportZip.Position = 0;
        var form = new MultipartFormDataContent();
        var streamContent = new StreamContent(exportZip);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(streamContent, "file", "export.zip");

        var response = await client.PostAsync("/workouts/import/export", form);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var started = await response.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);
        started!.Kind.Should().Be(ImportJobKinds.TempoExport);

        var job = await PollUntilTerminalAsync(client, started.Id);
        job.Status.Should().Be(ImportJobStatuses.Completed);
        job.Statistics.Should().NotBeNull();
        job.Statistics!.Workouts.Imported.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task TempoExport_MissingManifest_FailsJob()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var zipBytes = ExportTestHelper.CreateZipWithMissingManifestAsync().ToArray();
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(zipBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "file",
            FileName = "export.zip"
        };
        form.Add(file);

        var response = await client.PostAsync("/workouts/import/export", form);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var started = await response.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);

        var job = await PollUntilTerminalAsync(client, started!.Id);
        job.Status.Should().Be(ImportJobStatuses.Failed);
        job.ErrorMessage.Should().Contain("manifest");

        using var scope = _factory.Server.Services.CreateScope();
        var media = scope.ServiceProvider.GetRequiredService<MediaStorageConfig>();
        Directory.Exists(Path.Combine(media.RootPath, "imports", started.Id.ToString())).Should().BeFalse();
        (await scope.ServiceProvider.GetRequiredService<TempoDbContext>().ImportJobs.FindAsync(started.Id))
            .Should().NotBeNull();
    }

    [Fact]
    public async Task CrossKind_ActiveStravaBlocksTempoCreate()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var zipBytes = CreateZipBytes(includeCsv: true);

        var strava = await client.PostAsJsonAsync("/workouts/import/jobs", new
        {
            kind = ImportJobKinds.StravaBulk,
            filename = "strava.zip",
            byteSize = zipBytes.Length
        });
        strava.StatusCode.Should().Be(HttpStatusCode.Created);
        var active = await strava.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);

        var tempo = await client.PostAsJsonAsync("/workouts/import/jobs", new
        {
            kind = ImportJobKinds.TempoExport,
            filename = "tempo.zip",
            byteSize = zipBytes.Length
        });
        tempo.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var conflict = await tempo.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);
        conflict!.Id.Should().Be(active!.Id);

        (await client.DeleteAsync($"/workouts/import/jobs/{active.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TempoExport_CancelMidRestore_KeepsCommittedData()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        // Seed then export a multi-entity archive so cancel can land after progress
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedUserSettingsAsync(db);
            var shoe = await TestDataSeeder.SeedShoeAsync(db, "Nike", "Pegasus");
            for (var i = 0; i < 40; i++)
            {
                await TestDataSeeder.SeedWorkoutCompleteAsync(db, shoeId: shoe.Id, distanceM: 5000 + i, durationS: 1800 + i);
            }
        }

        var exportZip = await ImportTestHelper.CreateExportZipWithDataAsync(client);
        await EnsureCleanDatabaseAsync();

        exportZip.Position = 0;
        var form = new MultipartFormDataContent();
        var streamContent = new StreamContent(exportZip);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(streamContent, "file", "export.zip");

        var response = await client.PostAsync("/workouts/import/export", form);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var started = await response.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        ImportJobDocument? snapshot = null;
        var cancelSent = false;
        while (DateTime.UtcNow < deadline)
        {
            var poll = await client.GetAsync($"/workouts/import/jobs/{started!.Id}");
            snapshot = await poll.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);
            if (snapshot!.Status is ImportJobStatuses.Completed or ImportJobStatuses.Failed)
            {
                break;
            }

            if (!cancelSent &&
                snapshot.Processed >= 3 &&
                snapshot.Status == ImportJobStatuses.Running)
            {
                var cancel = await client.DeleteAsync($"/workouts/import/jobs/{started!.Id}");
                if (cancel.StatusCode == HttpStatusCode.OK)
                {
                    cancelSent = true;
                }
                else
                {
                    // Worker can complete between the Running poll and DELETE.
                    cancel.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                    var raced = await client.GetAsync($"/workouts/import/jobs/{started.Id}");
                    snapshot = await raced.Content.ReadFromJsonAsync<ImportJobDocument>(JsonOptions);
                    snapshot!.Status.Should().BeOneOf(ImportJobStatuses.Completed, ImportJobStatuses.Failed);
                    break;
                }
            }

            await Task.Delay(20);
        }

        snapshot.Should().NotBeNull();
        if (cancelSent)
        {
            var finished = await PollUntilTerminalAsync(client, started!.Id);
            finished.Status.Should().Be(ImportJobStatuses.Failed);
            finished.ErrorMessage.Should().Be(ImportJobErrorMessages.Cancelled);
        }

        using var verifyScope = _factory.Server.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TempoDbContext>();
        var media = verifyScope.ServiceProvider.GetRequiredService<MediaStorageConfig>();
        var entityCount = await verifyDb.Shoes.CountAsync() + await verifyDb.Workouts.CountAsync();
        entityCount.Should().BeGreaterThanOrEqualTo(1);
        Directory.Exists(Path.Combine(media.RootPath, "imports", started!.Id.ToString())).Should().BeFalse();
    }

    [Fact]
    public void ImportJobDocument_MapsResultJson_AndLeavesStravaNull()
    {
        var withResult = new ImportJob
        {
            Kind = ImportJobKinds.TempoExport,
            Status = ImportJobStatuses.Completed,
            Filename = "export.zip",
            ResultJson = """
                {
                  "statistics": {
                    "workouts": { "imported": 2, "skipped": 1, "errors": 0 },
                    "shoes": { "imported": 1, "skipped": 0, "errors": 0 }
                  },
                  "warnings": ["duplicate shoe"],
                  "errors": ["bad media"]
                }
                """
        };

        var doc = ImportJobDocument.FromEntity(withResult);
        doc.Statistics.Should().NotBeNull();
        doc.Statistics!.Workouts.Imported.Should().Be(2);
        doc.Statistics.Workouts.Skipped.Should().Be(1);
        doc.Statistics.Shoes.Imported.Should().Be(1);
        doc.Warnings.Should().Equal("duplicate shoe");
        doc.ErrorMessages.Should().Equal("bad media");

        var strava = ImportJobDocument.FromEntity(new ImportJob
        {
            Kind = ImportJobKinds.StravaBulk,
            Status = ImportJobStatuses.Completed,
            Filename = "export.zip",
            Successful = 1
        });
        strava.Statistics.Should().BeNull();
        strava.Warnings.Should().BeNull();
        strava.ErrorMessages.Should().BeNull();
    }

    private static async Task PutAllChunksAsync(HttpClient client, Guid jobId, byte[] zipBytes)
    {
        var total = Math.Max(1, (int)Math.Ceiling(zipBytes.Length / (double)ImportJobLimits.ChunkSizeBytes));
        for (var i = 0; i < total; i++)
        {
            var start = i * ImportJobLimits.ChunkSizeBytes;
            var length = Math.Min(ImportJobLimits.ChunkSizeBytes, zipBytes.Length - start);
            await PutChunkAsync(client, jobId, i, total, zipBytes.AsSpan(start, length).ToArray());
        }
    }

    private static async Task PutChunkAsync(HttpClient client, Guid jobId, int index, int total, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var response = await client.PutAsync($"/workouts/import/jobs/{jobId}/chunks/{index}?total={total}", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
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

    private static MultipartFormDataContent CreateZipForm(bool includeCsv, int activityCount = 1)
    {
        var zipBytes = CreateZipBytes(includeCsv, activityCount);
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

    private static byte[] CreateZipBytes(bool includeCsv, int activityCount = 1)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (includeCsv)
            {
                var csv = archive.CreateEntry("activities.csv");
                using var writer = new StreamWriter(csv.Open(), Encoding.UTF8);
                writer.WriteLine("Activity ID,Activity Date,Activity Name,Activity Type,Activity Description,Filename,Activity Private Note,Media");
                for (var i = 0; i < activityCount; i++)
                {
                    writer.WriteLine($"{i + 1},2024-01-15,Morning Run {i},Run,,activities/morning-{i}.gpx,,");
                }
            }

            for (var i = 0; i < activityCount; i++)
            {
                var gpx = archive.CreateEntry($"activities/morning-{i}.gpx");
                using var writer = new StreamWriter(gpx.Open(), Encoding.UTF8);
                writer.Write(GpxContentsFor(i));
            }
        }

        return stream.ToArray();
    }

    private static string GpxContentsFor(int index)
    {
        var hour = 10 + (index % 10);
        var day = 15 + (index / 10);
        return GpxContents
            .Replace("2024-01-15T10:", $"2024-01-{day:D2}T{hour:D2}:")
            .Replace("Morning Run", $"Morning Run {index}");
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
