using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.Services;

/// <summary>#218: backfills run until recorded complete, then stop running on every start.</summary>
public class BackfillRunnerTests : IAsyncLifetime
{
    private string _cs = null!;
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        _cs = await PostgresTestFixture.CreateCloneAsync();
        _provider = new ServiceCollection()
            .AddDbContext<TempoDbContext>(o => o.UseNpgsql(_cs))
            .BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await PostgresTestFixture.DropCloneAsync(_cs);
    }

    [Fact]
    public async Task Run_RecordsIdleBackfills_AndSkipsThemNextStart()
    {
        var idle = new FakeBackfill("idle", processed: 0);
        var busy = new FakeBackfill("busy", processed: 5);

        var first = await StartAndWaitAsync(StartupBackfillMode.Run, idle, busy);
        idle.Calls.Should().Be(1);
        busy.Calls.Should().Be(1);
        first.Should().Contain(m => m.StartsWith("Startup backfills: run 2 of 2: idle, busy"));
        (await CompletedNamesAsync()).Should().Equal("idle"); // busy did work: not done yet

        busy.Processed = 0; // its first pass finished the job
        var second = await StartAndWaitAsync(StartupBackfillMode.Run, idle, busy);
        idle.Calls.Should().Be(1, "recorded complete: not run again");
        busy.Calls.Should().Be(2);
        second.Should().Contain(m => m.StartsWith("Startup backfills: run 1 of 2: busy"));

        var third = await StartAndWaitAsync(StartupBackfillMode.Run, idle, busy);
        idle.Calls.Should().Be(1);
        busy.Calls.Should().Be(2);
        third.Should().Contain("Startup backfills: all 2 complete; none run");
    }

    [Fact]
    public async Task Run_VersionBump_RunsAgain()
    {
        var v1 = new FakeBackfill("timer", processed: 0, version: 1);
        await StartAndWaitAsync(StartupBackfillMode.Run, v1);

        var v2 = new FakeBackfill("timer", processed: 0, version: 2);
        await StartAndWaitAsync(StartupBackfillMode.Run, v2);

        v2.Calls.Should().Be(1, "a recorded v1 does not cover v2");
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        (await db.CompletedBackfills.SingleAsync()).Version.Should().Be(2);
    }

    [Fact]
    public async Task Run_FailedBackfill_IsNotRecorded_AndOthersStillRun()
    {
        var broken = new FakeBackfill("broken", processed: 0) { Throws = true };
        var fine = new FakeBackfill("fine", processed: 0);

        var logs = await StartAndWaitAsync(StartupBackfillMode.Run, broken, fine);

        fine.Calls.Should().Be(1);
        (await CompletedNamesAsync()).Should().Equal("fine");
        logs.Should().Contain("Backfill failed: broken");
    }

    [Fact]
    public async Task Skip_RunsNothingOnStart()
    {
        var backfill = new FakeBackfill("any", processed: 0);

        var logs = await StartAndWaitAsync(StartupBackfillMode.Skip, backfill, waitForFinish: false);

        backfill.Calls.Should().Be(0);
        logs.Should().Contain(m => m.StartsWith("Startup backfills: skip"));
    }

    [Fact]
    public async Task Force_RunsRecordedBackfillsAnyway()
    {
        var backfill = new FakeBackfill("done-already", processed: 0);
        await StartAndWaitAsync(StartupBackfillMode.Run, backfill);

        await StartAndWaitAsync(StartupBackfillMode.Force, backfill);

        backfill.Calls.Should().Be(2);
    }

    [Theory]
    [InlineData(StartupBackfillMode.Run)]
    [InlineData(StartupBackfillMode.Skip)]
    public async Task RestoreSignal_ClearsRecords_AndRunsEverything_InEveryMode(StartupBackfillMode mode)
    {
        var backfill = new FakeBackfill("cadence", processed: 0);
        await StartAndWaitAsync(StartupBackfillMode.Run, backfill); // recorded complete
        var callsBefore = backfill.Calls;

        // What ImportJobWorker does after a restore that imported workouts
        await using (var scope = _provider.CreateAsyncScope())
        {
            await StartupBackfills.ClearCompletedAsync(scope.ServiceProvider.GetRequiredService<TempoDbContext>());
        }

        (await CompletedNamesAsync()).Should().BeEmpty("a crash before the pass ends must still repair on next start");

        var signal = new BackfillRerunSignal();
        var logger = new ListLogger();
        var runner = new BackfillRunner(ScopeFactory, [backfill.Definition], mode, signal, logger);
        await runner.StartAsync(CancellationToken.None);
        await WaitForAsync(() => logger.Messages.Any(m => m.StartsWith("Backfills finished")) || mode == StartupBackfillMode.Skip);
        var startupCalls = backfill.Calls;

        signal.Request();
        await WaitForAsync(() => backfill.Calls > startupCalls && logger.Messages.Count(m => m.StartsWith("Backfills finished")) >= (mode == StartupBackfillMode.Skip ? 1 : 2));
        await runner.StopAsync(CancellationToken.None);

        backfill.Calls.Should().BeGreaterThan(startupCalls, "restore runs the backfills even under skip");
        backfill.Calls.Should().BeGreaterThan(callsBefore);
        (await CompletedNamesAsync()).Should().Equal("cadence");
    }

    [Fact]
    public async Task UnreachableDatabase_LogsAndKeepsRunning_InsteadOfStoppingTheHost()
    {
        await using var unreachable = new ServiceCollection()
            .AddDbContext<TempoDbContext>(o => o.UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1"))
            .BuildServiceProvider();
        var logger = new ListLogger();
        var runner = new BackfillRunner(
            unreachable.GetRequiredService<IServiceScopeFactory>(),
            [new FakeBackfill("any", processed: 0).Definition],
            StartupBackfillMode.Run,
            new BackfillRerunSignal(),
            logger);

        await runner.StartAsync(CancellationToken.None);
        await WaitForAsync(() => logger.Messages.Any(m => m.StartsWith("Startup backfills could not start")));

        // A faulted ExecuteTask is what makes .NET stop the host
        runner.ExecuteTask!.IsFaulted.Should().BeFalse();
        await runner.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(null, StartupBackfillMode.Run)]
    [InlineData(" ", StartupBackfillMode.Run)]
    [InlineData("RUN", StartupBackfillMode.Run)]
    [InlineData(" skip ", StartupBackfillMode.Skip)]
    [InlineData("Force", StartupBackfillMode.Force)]
    public void Parse_AcceptsKnownValues(string? value, StartupBackfillMode expected)
    {
        StartupBackfills.Parse(value).Should().Be(expected);
    }

    [Fact]
    public void Parse_Unknown_Throws()
    {
        var act = () => StartupBackfills.Parse("always");
        act.Should().Throw<InvalidOperationException>().WithMessage("*run, skip, force*");
    }

    [Fact]
    public void All_HasSevenUniquelyNamedBackfills()
    {
        StartupBackfills.All.Should().HaveCount(7);
        StartupBackfills.All.Select(b => b.Name).Should().OnlyHaveUniqueItems();
    }

    private IServiceScopeFactory ScopeFactory => _provider.GetRequiredService<IServiceScopeFactory>();

    private async Task<List<string>> StartAndWaitAsync(
        StartupBackfillMode mode,
        FakeBackfill first,
        FakeBackfill? second = null,
        bool waitForFinish = true)
    {
        var backfills = second is null ? [first.Definition] : new List<BackfillDefinition> { first.Definition, second.Definition };
        var logger = new ListLogger();
        var runner = new BackfillRunner(ScopeFactory, backfills, mode, new BackfillRerunSignal(), logger);

        await runner.StartAsync(CancellationToken.None);
        await WaitForAsync(() => logger.Messages.Any(m =>
            m.StartsWith("Backfills finished") || m.Contains("none run") || (!waitForFinish && m.StartsWith("Startup backfills"))));
        await runner.StopAsync(CancellationToken.None);
        return logger.Messages;
    }

    private Task<List<string>> StartAndWaitAsync(StartupBackfillMode mode, FakeBackfill only, bool waitForFinish) =>
        StartAndWaitAsync(mode, only, null, waitForFinish);

    private async Task<List<string>> CompletedNamesAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        return await db.CompletedBackfills.OrderBy(c => c.Name).Select(c => c.Name).ToListAsync();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 250 && !condition(); i++)
        {
            await Task.Delay(20);
        }

        condition().Should().BeTrue("the runner should reach the expected state within 5s");
    }

    private sealed class FakeBackfill(string name, int processed, int version = 1)
    {
        private int _calls;

        public int Processed { get; set; } = processed;
        public bool Throws { get; init; }
        public int Calls => Volatile.Read(ref _calls);

        public BackfillDefinition Definition => new(name, version, (_, _) =>
        {
            Interlocked.Increment(ref _calls);
            return Throws ? throw new InvalidOperationException("boom") : Task.FromResult(Processed);
        });
    }

    private sealed class ListLogger : ILogger<BackfillRunner>
    {
        private readonly object _gate = new();
        private readonly List<string> _messages = new();

        public List<string> Messages
        {
            get { lock (_gate) { return _messages.ToList(); } }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_gate) { _messages.Add(formatter(state, exception)); }
        }
    }
}
