using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tempo.Api.Data;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class DeviceNameBackfillServiceTests : IAsyncLifetime
{
    private string _cloneConnectionString = null!;
    private TempoDbContext _db = null!;
    private ListLogger<DeviceNameBackfillService> _logger = null!;
    private DeviceNameBackfillService _service = null!;

    public async Task InitializeAsync()
    {
        _cloneConnectionString = await PostgresTestFixture.CreateCloneAsync();
        _db = PostgresTestFixture.CreateContext(_cloneConnectionString);
        _logger = new ListLogger<DeviceNameBackfillService>();
        _service = new DeviceNameBackfillService(_db, _logger);
    }

    public async Task DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        if (_cloneConnectionString is not null)
        {
            await PostgresTestFixture.DropCloneAsync(_cloneConnectionString);
        }
    }

    [Fact]
    public async Task RunAsync_IsNoOp_WhenNoCandidates()
    {
        (await _service.RunAsync()).Should().Be(0);
        _logger.Messages.Should().Contain("Device name backfill: 0 of 0");
    }

    [Fact]
    public async Task RunAsync_RenamesBrandOnlyDevices_FromStoredFitDeviceInfo()
    {
        var fenix8 = await SeedAsync("Garmin", """{"device":{"manufacturer":1,"product":4536}}""");
        var coros = await SeedAsync("Garmin", """{"device":{"manufacturer":294,"product":5}}""");
        var stryd = await SeedAsync("Product 7", """{"device":{"manufacturer":95,"product":7}}""");
        var unknown = await SeedAsync("Garmin", """{"device":{"manufacturer":1,"product":65000}}""");

        (await _service.RunAsync()).Should().Be(4);

        (await DeviceAsync(fenix8)).Should().Be("Garmin Fenix 8");
        (await DeviceAsync(coros)).Should().Be("Coros");
        (await DeviceAsync(stryd)).Should().Be("Stryd");
        (await DeviceAsync(unknown)).Should().Be("Garmin (product 65000)");
        _logger.Messages.Should().Contain("Device name backfill: 4 of 4");
    }

    [Fact]
    public async Task RunAsync_LeavesSpecificAndNonFitNamesAlone()
    {
        var named = await SeedAsync("Garmin Forerunner 965", """{"device":{"manufacturer":1,"product":4536}}""");
        var apple = await SeedAsync("Apple Watch", """{"device":{"manufacturer":1,"product":4536}}""");
        var noFit = await SeedAsync("Garmin", rawFitData: null);
        var noDevice = await SeedAsync("Garmin", """{"session":{}}""");
        var noProduct = await SeedAsync("Garmin", """{"device":{"manufacturer":1}}""");

        (await _service.RunAsync()).Should().Be(0);

        (await DeviceAsync(named)).Should().Be("Garmin Forerunner 965");
        (await DeviceAsync(apple)).Should().Be("Apple Watch");
        (await DeviceAsync(noFit)).Should().Be("Garmin");
        (await DeviceAsync(noDevice)).Should().Be("Garmin");
        (await DeviceAsync(noProduct)).Should().Be("Garmin");
        _logger.Messages.Should().Contain("Device name backfill: 0 of 0");
    }

    [Fact]
    public async Task RunAsync_SecondRunIsIdle()
    {
        await SeedAsync("Garmin", """{"device":{"manufacturer":1,"product":4536}}""");

        (await _service.RunAsync()).Should().Be(1);
        (await _service.RunAsync()).Should().Be(0);
        _logger.Messages.Should().EndWith("Device name backfill: 0 of 0");
    }

    private async Task<Guid> SeedAsync(string device, string? rawFitData)
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db);
        workout.Device = device;
        workout.RawFitData = rawFitData;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return workout.Id;
    }

    private Task<string?> DeviceAsync(Guid id) =>
        _db.Workouts.AsNoTracking().Where(w => w.Id == id).Select(w => w.Device).SingleAsync();

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
