using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

[Collection("Integration Tests")]
public class IntervalsIcuSettingsEndpointsTests : IClassFixture<IntervalsIcuSettingsTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IntervalsIcuSettingsTestFactory _factory;

    public IntervalsIcuSettingsEndpointsTests(IntervalsIcuSettingsTestFactory factory)
    {
        _factory = factory;
        _factory.Fake.Reset();
        _factory.Decorated.Services.GetRequiredService<IntervalsIcuSyncQueue>().Reset();
    }

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        await EnsureCleanDatabaseAsync();
        return await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory.Decorated);
    }

    private async Task EnsureCleanDatabaseAsync()
    {
        using var scope = _factory.Decorated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: true);
    }

    [Fact]
    public async Task Get_WithNoRow_ReturnsDisconnected()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.GetAsync("/settings/intervals-icu");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("apiKey");
        var body = JsonSerializer.Deserialize<IntervalsIcuStatusResponse>(json, JsonOptions);
        body.Should().NotBeNull();
        body!.Connected.Should().BeFalse();
    }

    [Fact]
    public async Task Put_WithValidKey_PersistsCiphertextAndOmitsKeyOnGet()
    {
        var client = await AuthenticatedClientAsync();
        _factory.Fake.Result = IntervalsIcuProbeResult.Ok;

        var put = await client.PutAsJsonAsync("/settings/intervals-icu", new { apiKey = "plain-icu-key" });
        var putJson = await put.Content.ReadAsStringAsync();

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        putJson.Should().NotContain("apiKey");
        putJson.Should().NotContain("plain-icu-key");
        var created = JsonSerializer.Deserialize<IntervalsIcuStatusResponse>(putJson, JsonOptions);
        created.Should().NotBeNull();
        created!.Connected.Should().BeTrue();
        created.Enabled.Should().BeTrue();

        var get = await client.GetAsync("/settings/intervals-icu");
        var getJson = await get.Content.ReadAsStringAsync();
        getJson.Should().NotContain("apiKey");
        getJson.Should().NotContain("plain-icu-key");
        var status = JsonSerializer.Deserialize<IntervalsIcuStatusResponse>(getJson, JsonOptions);
        status!.Connected.Should().BeTrue();
        status.Enabled.Should().BeTrue();

        using var scope = _factory.Decorated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        var row = await db.IntervalsIcuConnections.SingleAsync();
        row.ApiKeyCiphertext.Should().NotBeEmpty();
        System.Text.Encoding.UTF8.GetString(row.ApiKeyCiphertext).Should().NotContain("plain-icu-key");
        row.Enabled.Should().BeTrue();
        _factory.Fake.LastApiKey.Should().Be("plain-icu-key");
    }

    [Fact]
    public async Task Put_WhenProbeUnauthorized_Returns400AndWritesNoRow()
    {
        var client = await AuthenticatedClientAsync();
        _factory.Fake.Result = IntervalsIcuProbeResult.Unauthorized;

        var put = await client.PutAsJsonAsync("/settings/intervals-icu", new { apiKey = "bad-key" });

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var scope = _factory.Decorated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        (await db.IntervalsIcuConnections.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Put_WhenAlreadyConnected_ReplacesCiphertextOnly()
    {
        var client = await AuthenticatedClientAsync();
        _factory.Fake.Result = IntervalsIcuProbeResult.Ok;
        (await client.PutAsJsonAsync("/settings/intervals-icu", new { apiKey = "first-key" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        DateTime connectedAt;
        DateTime lastSuccess;
        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var row = await db.IntervalsIcuConnections.SingleAsync();
            connectedAt = row.ConnectedAt;
            lastSuccess = DateTime.UtcNow.AddDays(-3);
            row.LastSuccessfulSyncAt = lastSuccess;
            await db.SaveChangesAsync();
        }

        _factory.Fake.Reset();
        _factory.Fake.Result = IntervalsIcuProbeResult.Ok;
        var second = await client.PutAsJsonAsync("/settings/intervals-icu", new { apiKey = "second-key" });

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Fake.LastApiKey.Should().Be("second-key");
        using var verify = _factory.Decorated.Services.CreateScope();
        var stored = await verify.ServiceProvider.GetRequiredService<TempoDbContext>()
            .IntervalsIcuConnections.SingleAsync();
        stored.ConnectedAt.Should().Be(connectedAt);
        stored.LastSuccessfulSyncAt.Should().BeCloseTo(lastSuccess, TimeSpan.FromSeconds(1));
        stored.Enabled.Should().BeTrue();
        verify.ServiceProvider.GetRequiredService<IntervalsIcuSecretProtector>()
            .Decrypt(stored.ApiKeyCiphertext).Should().Be("second-key");
    }

    [Fact]
    public async Task Put_WhenBlankKey_Returns400()
    {
        var client = await AuthenticatedClientAsync();

        var put = await client.PutAsJsonAsync("/settings/intervals-icu", new { apiKey = "   " });

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Fake.ProbeCount.Should().Be(0);
    }

    [Fact]
    public async Task Delete_RemovesRow_AndSecondDeleteIs204()
    {
        var client = await AuthenticatedClientAsync();
        _factory.Fake.Result = IntervalsIcuProbeResult.Ok;
        (await client.PutAsJsonAsync("/settings/intervals-icu", new { apiKey = "plain-icu-key" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var first = await client.DeleteAsync("/settings/intervals-icu");
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            (await db.IntervalsIcuConnections.CountAsync()).Should().Be(0);
        }

        var second = await client.DeleteAsync("/settings/intervals-icu");
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task PostSync_WithNoRow_Returns204AndDoesNotFetchFile()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.PostAsync("/settings/intervals-icu/sync", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _factory.Fake.GetFileCount.Should().Be(0);
        _factory.Fake.ListCount.Should().Be(0);
    }

    [Fact]
    public async Task PostSync_WhenConnected_Returns202WithoutFetchingFile()
    {
        var client = await AuthenticatedClientAsync();
        _factory.Fake.Result = IntervalsIcuProbeResult.Ok;
        (await client.PutAsJsonAsync("/settings/intervals-icu", new { apiKey = "plain-icu-key" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Fake.Reset();
        _factory.Fake.Result = IntervalsIcuProbeResult.Ok;

        var response = await client.PostAsync("/settings/intervals-icu/sync", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _factory.Fake.GetFileCount.Should().Be(0);
        _factory.Fake.ListCount.Should().Be(0);
    }

    [Fact]
    public async Task RunTick_ImportsNewRunWithIdentity()
    {
        var client = await AuthenticatedClientAsync();
        _factory.Fake.Result = IntervalsIcuProbeResult.Ok;
        (await client.PutAsJsonAsync("/settings/intervals-icu", new { apiKey = "plain-icu-key" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var fitBytes = await File.ReadAllBytesAsync(FitFixturePath());
        _factory.Fake.Activities.Add(new IntervalsIcuActivity
        {
            Id = "icu-42",
            Type = "Run",
            FileType = "fit"
        });
        _factory.Fake.Files["icu-42"] = new IntervalsIcuActivityFile
        {
            FileName = "activity.fit",
            Bytes = fitBytes
        };

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var sync = scope.ServiceProvider.GetRequiredService<IntervalsIcuSyncService>();
            await sync.RunTickAsync();
        }

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await db.Workouts.SingleAsync();
            workout.Source.Should().Be(WorkoutExternalSource.IntervalsIcu);
            var identity = await db.WorkoutExternalIdentities.SingleAsync();
            identity.Source.Should().Be(WorkoutExternalSource.IntervalsIcu);
            identity.ExternalId.Should().Be("icu-42");
            identity.WorkoutId.Should().Be(workout.Id);
        }

        var get = await client.GetAsync("/settings/intervals-icu");
        var status = JsonSerializer.Deserialize<IntervalsIcuStatusResponse>(
            await get.Content.ReadAsStringAsync(),
            JsonOptions);
        status!.LastSuccessfulSyncAt.Should().NotBeNull();
        status.LastSyncAttemptAt.Should().NotBeNull();
        _factory.Fake.LastOldest.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2));
    }

    [Fact]
    public async Task RunTick_SkipsNonRunsAndJunkFileTypes()
    {
        var client = await AuthenticatedClientAsync();
        await ConnectAndResetFakeAsync(client);

        var fitBytes = await File.ReadAllBytesAsync(FitFixturePath());
        AddListedActivity("ride-1", "Ride", "fit", fitBytes);
        AddListedActivity("walk-1", "Walk", "fit", fitBytes);
        AddListedActivity("empty-type", null, "fit", fitBytes);
        AddListedActivity("empty-file", "Run", null, fitBytes);
        AddListedActivity("tcx-1", "Run", "tcx", fitBytes);
        AddListedActivity("icu-run", "Run", "fit", fitBytes);

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var sync = scope.ServiceProvider.GetRequiredService<IntervalsIcuSyncService>();
            await sync.RunTickAsync();
        }

        _factory.Fake.GetFileCount.Should().Be(1);
        using var dbScope = _factory.Decorated.Services.CreateScope();
        var db = dbScope.ServiceProvider.GetRequiredService<TempoDbContext>();
        var workout = await db.Workouts.SingleAsync();
        var identity = await db.WorkoutExternalIdentities.SingleAsync();
        identity.Source.Should().Be(WorkoutExternalSource.IntervalsIcu);
        identity.ExternalId.Should().Be("icu-run");
        identity.WorkoutId.Should().Be(workout.Id);
    }

    [Fact]
    public async Task RunTick_SameActivityId_DoesNotFetchFileAgain()
    {
        var client = await AuthenticatedClientAsync();
        await ConnectAndResetFakeAsync(client);

        var fitBytes = await File.ReadAllBytesAsync(FitFixturePath());
        AddListedActivity("icu-42", "Run", "fit", fitBytes);

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IntervalsIcuSyncService>().RunTickAsync();
        }

        _factory.Fake.GetFileCount.Should().Be(1);

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IntervalsIcuSyncService>().RunTickAsync();
        }

        _factory.Fake.GetFileCount.Should().Be(1);
        using var dbScope = _factory.Decorated.Services.CreateScope();
        var db = dbScope.ServiceProvider.GetRequiredService<TempoDbContext>();
        (await db.Workouts.CountAsync()).Should().Be(1);
        (await db.WorkoutExternalIdentities.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RunTick_StatsKeyMatch_AttachesIdentityWithoutRenamingSource()
    {
        var client = await AuthenticatedClientAsync();
        await ConnectAndResetFakeAsync(client);

        var fitBytes = await File.ReadAllBytesAsync(FitFixturePath());
        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var intake = scope.ServiceProvider.GetRequiredService<WorkoutIntake>();
            await using var stream = new MemoryStream(fitBytes, writable: false);
            var created = await intake.ProcessAsync(stream, "running-cadence-80.fit");
            created.Action.Should().Be("created");
            created.Workout!.Source.Should().Be("fit_import");
        }

        AddListedActivity("icu-attach", "Run", "fit", fitBytes);

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IntervalsIcuSyncService>().RunTickAsync();
        }

        _factory.Fake.GetFileCount.Should().Be(1);
        using var dbScope = _factory.Decorated.Services.CreateScope();
        var db = dbScope.ServiceProvider.GetRequiredService<TempoDbContext>();
        var workout = await db.Workouts.SingleAsync();
        workout.Source.Should().Be("fit_import");
        var identity = await db.WorkoutExternalIdentities.SingleAsync();
        identity.Source.Should().Be(WorkoutExternalSource.IntervalsIcu);
        identity.ExternalId.Should().Be("icu-attach");
        identity.WorkoutId.Should().Be(workout.Id);
    }

    [Fact]
    public async Task RunTick_WhenListUnauthorized_DisablesAndSyncReturns204()
    {
        var client = await AuthenticatedClientAsync();
        await ConnectAndResetFakeAsync(client);
        _factory.Fake.ListStatus = IntervalsIcuProbeResult.Unauthorized;

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IntervalsIcuSyncService>().RunTickAsync();
        }

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var row = await db.IntervalsIcuConnections.SingleAsync();
            row.Enabled.Should().BeFalse();
            row.LastError.Should().NotBeNullOrWhiteSpace();
            scope.ServiceProvider.GetRequiredService<IntervalsIcuSecretProtector>()
                .Decrypt(row.ApiKeyCiphertext).Should().Be("plain-icu-key");
        }

        (await client.PostAsync("/settings/intervals-icu/sync", content: null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        _factory.Fake.GetFileCount.Should().Be(0);
    }

    [Fact]
    public async Task PostEnable_ReenablesOrRejectsWithoutRepaste()
    {
        var client = await AuthenticatedClientAsync();
        (await client.PostAsync("/settings/intervals-icu/enable", content: null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        await ConnectAndResetFakeAsync(client);
        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var row = await db.IntervalsIcuConnections.SingleAsync();
            row.Enabled = false;
            row.LastError = "intervals.icu rejected the API key";
            await db.SaveChangesAsync();
        }

        _factory.Fake.Result = IntervalsIcuProbeResult.Unauthorized;
        (await client.PostAsync("/settings/intervals-icu/enable", content: null))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<TempoDbContext>()
                .IntervalsIcuConnections.SingleAsync()).Enabled.Should().BeFalse();
        }

        _factory.Fake.Result = IntervalsIcuProbeResult.Ok;
        var enabled = await client.PostAsync("/settings/intervals-icu/enable", content: null);
        enabled.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonSerializer.Deserialize<IntervalsIcuStatusResponse>(
            await enabled.Content.ReadAsStringAsync(),
            JsonOptions);
        body!.Enabled.Should().BeTrue();
        body.LastError.Should().BeNull();
    }

    [Fact]
    public async Task PostSync_WhenEnabled_CoalescesSecondWake()
    {
        var client = await AuthenticatedClientAsync();
        await ConnectAndResetFakeAsync(client);
        var queue = _factory.Decorated.Services.GetRequiredService<IntervalsIcuSyncQueue>();

        var first = await client.PostAsync("/settings/intervals-icu/sync", content: null);
        var second = await client.PostAsync("/settings/intervals-icu/sync", content: null);

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        queue.TryWake().Should().BeFalse();
        _factory.Fake.GetFileCount.Should().Be(0);

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IntervalsIcuSyncService>().RunTickAsync();
        }

        _factory.Fake.GetFileCount.Should().Be(0);
        queue.TryWake().Should().BeFalse();
    }

    [Fact]
    public async Task RunTick_ListWindow_UsesConnectMinusTwoThenCapsAtFourteenDays()
    {
        var client = await AuthenticatedClientAsync();
        await ConnectAndResetFakeAsync(client);

        DateTime connectedAt;
        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            connectedAt = (await db.IntervalsIcuConnections.SingleAsync()).ConnectedAt;
            await scope.ServiceProvider.GetRequiredService<IntervalsIcuSyncService>().RunTickAsync();
        }

        _factory.Fake.LastOldest.Should().Be(DateOnly.FromDateTime(connectedAt).AddDays(-2));

        using (var scope = _factory.Decorated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var row = await db.IntervalsIcuConnections.SingleAsync();
            row.ConnectedAt = DateTime.UtcNow.AddDays(-30);
            row.LastSuccessfulSyncAt = DateTime.UtcNow.AddDays(-20);
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IntervalsIcuSyncService>().RunTickAsync();
        }

        _factory.Fake.LastOldest.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-14));
    }

    [Fact]
    public async Task Unauthenticated_GetPutDelete_Return401()
    {
        await EnsureCleanDatabaseAsync();
        var client = TestHttpClientFactory.CreateUnauthenticatedClient(_factory.Decorated);

        (await client.GetAsync("/settings/intervals-icu")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PutAsJsonAsync("/settings/intervals-icu", new { apiKey = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.DeleteAsync("/settings/intervals-icu")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsync("/settings/intervals-icu/sync", content: null))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsync("/settings/intervals-icu/enable", content: null))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task ConnectAndResetFakeAsync(HttpClient client)
    {
        _factory.Fake.Result = IntervalsIcuProbeResult.Ok;
        (await client.PutAsJsonAsync("/settings/intervals-icu", new { apiKey = "plain-icu-key" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Fake.Reset();
        _factory.Fake.Result = IntervalsIcuProbeResult.Ok;
    }

    private void AddListedActivity(string id, string? type, string? fileType, byte[] fitBytes)
    {
        _factory.Fake.Activities.Add(new IntervalsIcuActivity
        {
            Id = id,
            Type = type,
            FileType = fileType
        });
        _factory.Fake.Files[id] = new IntervalsIcuActivityFile
        {
            FileName = "activity.fit",
            Bytes = fitBytes
        };
    }

    private static string FitFixturePath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "running-cadence-80.fit");
        File.Exists(path).Should().BeTrue("running-cadence-80.fit must be copied to the test output directory");
        return path;
    }

    private sealed class IntervalsIcuStatusResponse
    {
        public bool Connected { get; set; }
        public bool? Enabled { get; set; }
        public DateTime? LastSuccessfulSyncAt { get; set; }
        public DateTime? LastSyncAttemptAt { get; set; }
        public string? LastError { get; set; }
    }
}

public sealed class IntervalsIcuSettingsTestFactory : IDisposable
{
    private readonly TempoWebApplicationFactory _inner = new();

    public FakeIntervalsIcuClient Fake { get; } = new();

    public WebApplicationFactory<Program> Decorated { get; }

    public IntervalsIcuSettingsTestFactory()
    {
        Decorated = _inner.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IIntervalsIcuClient>();
                services.AddSingleton<IIntervalsIcuClient>(Fake);
            });
        });
    }

    public void Dispose()
    {
        Decorated.Dispose();
        _inner.Dispose();
    }
}
