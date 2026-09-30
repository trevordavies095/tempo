using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tempo.Api.Data;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class RoutePreviewBackfillServiceTests : IAsyncLifetime
{
    private string _cloneConnectionString = null!;
    private TempoDbContext _db = null!;
    private ListLogger<RoutePreviewBackfillService> _logger = null!;
    private RoutePreviewBackfillService _service = null!;

    public async Task InitializeAsync()
    {
        _cloneConnectionString = await PostgresTestFixture.CreateCloneAsync();
        _db = PostgresTestFixture.CreateContext(_cloneConnectionString);
        _logger = new ListLogger<RoutePreviewBackfillService>();
        _service = new RoutePreviewBackfillService(_db, _logger);
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
    public async Task RunAsync_IsNoOp_WhenNoNullPreviews()
    {
        var processed = await _service.RunAsync();

        processed.Should().Be(0);
        _logger.Messages.Should().Contain("Workout route preview backfill: 0 of 0");
    }

    [Fact]
    public async Task RunAsync_ProcessesNullPreviewsInBatches_AndIsIdempotent()
    {
        var total = RoutePreviewBackfillService.BatchSize + 5;
        for (var i = 0; i < total; i++)
        {
            await SeedRouteAsync(previewGeoJson: null, pointCount: 12);
        }

        var first = await _service.RunAsync();
        first.Should().Be(total);

        var routes = await _db.WorkoutRoutes.ToListAsync();
        routes.Should().HaveCount(total);
        routes.Should().OnlyContain(r => r.PreviewGeoJson != null);
        routes.Should().OnlyContain(r =>
            r.PreviewGeoJson == TrackGeometry.BuildRoutePreviewGeoJson(r.RouteGeoJson));

        _logger.Messages.Should().Contain($"Workout route preview backfill: {RoutePreviewBackfillService.BatchSize} of {total}");
        _logger.Messages.Should().Contain($"Workout route preview backfill: {total} of {total}");

        _logger.Messages.Clear();
        var second = await _service.RunAsync();
        second.Should().Be(0);
        _logger.Messages.Should().Contain("Workout route preview backfill: 0 of 0");
        (await _db.WorkoutRoutes.CountAsync(r => r.PreviewGeoJson == null)).Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_SkipsSentinelPreviews()
    {
        await SeedRouteAsync(previewGeoJson: null, pointCount: 8);
        await SeedRouteAsync(previewGeoJson: TrackGeometry.EmptyRoutePreviewSentinel, pointCount: 8);
        await SeedRouteAsync(
            previewGeoJson: TrackGeometry.EmptyRoutePreviewSentinel,
            routeGeoJson: "{}");

        var processed = await _service.RunAsync();

        processed.Should().Be(1);
        var routes = await _db.WorkoutRoutes.OrderBy(r => r.Id).ToListAsync();
        routes.Count(r => r.PreviewGeoJson == TrackGeometry.EmptyRoutePreviewSentinel).Should().Be(2);
        routes.Count(r => r.PreviewGeoJson != null && r.PreviewGeoJson != TrackGeometry.EmptyRoutePreviewSentinel)
            .Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_WritesSentinel_ForEmptyRouteGeoJson()
    {
        await SeedRouteAsync(previewGeoJson: null, routeGeoJson: "{}");

        var processed = await _service.RunAsync();

        processed.Should().Be(1);
        var route = await _db.WorkoutRoutes.SingleAsync();
        route.PreviewGeoJson.Should().Be(TrackGeometry.EmptyRoutePreviewSentinel);

        (await _service.RunAsync()).Should().Be(0);
    }

    private async Task SeedRouteAsync(
        string? previewGeoJson,
        int pointCount = 5,
        string? routeGeoJson = null)
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(
            _db,
            startedAt: DateTime.UtcNow.AddMinutes(-_seedOffset++));
        var coordinates = new List<double[]>();
        for (var i = 0; i < pointCount; i++)
        {
            coordinates.Add(new[] { 0.0 + (i * 0.001), 0.0 + (i * 0.001) });
        }

        var route = await TestDataSeeder.SeedWorkoutWithRouteAsync(_db, workout, coordinates);
        if (routeGeoJson != null)
        {
            route.RouteGeoJson = routeGeoJson;
        }

        route.PreviewGeoJson = previewGeoJson;
        await _db.SaveChangesAsync();
    }

    private int _seedOffset;

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose()
            {
            }
        }
    }
}
