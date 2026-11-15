using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tempo.Api.Commands;
using Tempo.Api.Data;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.Services;

/// <summary>
/// #218: backfills are off the default start path. They run from a Tempo ZIP restore, the
/// <c>backfill</c> command, or the opt-in run/force startup modes, and record completion.
/// </summary>
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

    // --- BackfillPass (shared by restore, the command, and run/force) ---

    [Fact]
    public async Task Pass_RecordsIdle_AndBusyThenIdle_InOnePass()
    {
        var idle = new FakeBackfill("idle", 0);
        var busyOnce = new FakeBackfill("busy-once", [5, 0]);

        var recorded = await BackfillPass.RunAsync(ScopeFactory, [idle.Definition, busyOnce.Definition], new ListLogger(), default);

        recorded.Should().Be(2);
        idle.Calls.Should().Be(1);
        busyOnce.Calls.Should().Be(2, "a backfill that did work runs once more to confirm it's idle");
        (await CompletedNamesAsync()).Should().BeEquivalentTo("idle", "busy-once");
    }

    [Fact]
    public async Task Pass_StillBusyAfterTwoRuns_OrThrowing_IsNotRecorded_OthersStillRun()
    {
        var stuck = new FakeBackfill("stuck", [3, 3]);
        var broken = new FakeBackfill("broken", 0) { Throws = true };
        var fine = new FakeBackfill("fine", 0);
        var logger = new ListLogger();

        var recorded = await BackfillPass.RunAsync(
            ScopeFactory, [stuck.Definition, broken.Definition, fine.Definition], logger, default);

        recorded.Should().Be(1);
        stuck.Calls.Should().Be(2);
        (await CompletedNamesAsync()).Should().Equal("fine");
        logger.Messages.Should().Contain("Backfill failed: broken");
        logger.Messages.Should().Contain(m => m.StartsWith("Backfills finished: 1 of 3 recorded complete; 2 still pending"));
    }

    [Fact]
    public async Task Pending_VersionBump_IsPendingAgain()
    {
        await BackfillPass.RunAsync(ScopeFactory, [new FakeBackfill("timer", 0, version: 1).Definition], new ListLogger(), default);

        await using var scope = _provider.CreateAsyncScope();
        var completed = await BackfillPass.LoadCompletedAsync(scope.ServiceProvider.GetRequiredService<TempoDbContext>());

        BackfillPass.Pending([new FakeBackfill("timer", 0, version: 1).Definition], completed).Should().BeEmpty();
        BackfillPass.Pending([new FakeBackfill("timer", 0, version: 2).Definition], completed).Should().ContainSingle();
    }

    // --- Default start: check only ---

    [Fact]
    public async Task Check_AllComplete_LogsAndRunsNothing()
    {
        var backfill = new FakeBackfill("route", 0);
        await BackfillPass.RunAsync(ScopeFactory, [backfill.Definition], new ListLogger(), default);
        var logger = new ListLogger();

        await using var scope = _provider.CreateAsyncScope();
        await StartupBackfills.CheckAsync(scope.ServiceProvider.GetRequiredService<TempoDbContext>(), [backfill.Definition], logger);

        backfill.Calls.Should().Be(1, "the check never runs a backfill");
        logger.Messages.Should().Contain("Startup backfills: all 1 complete; none run");
    }

    [Fact]
    public async Task Check_NoWorkouts_RecordsAllComplete_WithoutRunning()
    {
        var a = new FakeBackfill("a", 5);
        var b = new FakeBackfill("b", 5);
        var logger = new ListLogger();

        await using (var scope = _provider.CreateAsyncScope())
        {
            await StartupBackfills.CheckAsync(scope.ServiceProvider.GetRequiredService<TempoDbContext>(), [a.Definition, b.Definition], logger);
        }

        a.Calls.Should().Be(0);
        b.Calls.Should().Be(0);
        (await CompletedNamesAsync()).Should().BeEquivalentTo("a", "b");
        logger.Messages.Should().Contain(m => m.Contains("no workouts yet"));
    }

    [Fact]
    public async Task Check_WorkoutsAndPending_WarnsWithCommand_RecordsNothing()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        await TestDataSeeder.SeedWorkoutAsync(db);
        var backfill = new FakeBackfill("device lap", 5);
        var logger = new ListLogger();

        await StartupBackfills.CheckAsync(db, [backfill.Definition], logger);

        backfill.Calls.Should().Be(0);
        (await CompletedNamesAsync()).Should().BeEmpty();
        logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain("1 of 1 have not completed").And.Contain("device lap").And.Contain(StartupBackfills.CatchUpCommand);
    }

    // --- Registration and modes ---

    [Fact]
    public void Default_IsSkip_AndRegistersNoHostedService()
    {
        StartupBackfills.Parse(null).Should().Be(StartupBackfillMode.Skip);

        var services = new ServiceCollection().AddStartupBackfills(StartupBackfillMode.Skip);

        services.Should().NotContain(d => d.ServiceType == typeof(IHostedService));
        services.Should().Contain(d => d.ServiceType == typeof(IReadOnlyList<BackfillDefinition>), "restore and the command still need them");
    }

    [Theory]
    [InlineData(StartupBackfillMode.Run)]
    [InlineData(StartupBackfillMode.Force)]
    public void OptInModes_RegisterTheOneShotRunner(StartupBackfillMode mode)
    {
        new ServiceCollection().AddStartupBackfills(mode)
            .Should().ContainSingle(d => d.ServiceType == typeof(IHostedService))
            .Which.ImplementationType.Should().Be(typeof(BackfillRunner));
    }

    [Theory]
    [InlineData(" ", StartupBackfillMode.Skip)]
    [InlineData("SKIP", StartupBackfillMode.Skip)]
    [InlineData(" run ", StartupBackfillMode.Run)]
    [InlineData("Force", StartupBackfillMode.Force)]
    public void Parse_AcceptsKnownValues(string value, StartupBackfillMode expected)
    {
        StartupBackfills.Parse(value).Should().Be(expected);
    }

    [Fact]
    public void Parse_Unknown_Throws()
    {
        var act = () => StartupBackfills.Parse("always");
        act.Should().Throw<InvalidOperationException>().WithMessage("*skip, run, force*");
    }

    [Fact]
    public async Task Runner_Run_RunsPendingOnly_ThenExits()
    {
        var done = new FakeBackfill("done", 0);
        await BackfillPass.RunAsync(ScopeFactory, [done.Definition], new ListLogger(), default);
        var pending = new FakeBackfill("pending", 0);
        var logger = new ListLogger();
        var runner = new BackfillRunner(ScopeFactory, [done.Definition, pending.Definition], StartupBackfillMode.Run, logger);

        await runner.StartAsync(CancellationToken.None);
        await runner.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));

        done.Calls.Should().Be(1);
        pending.Calls.Should().Be(1);
        logger.Messages.Should().Contain(m => m.StartsWith("Startup backfills: run 1 of 2: pending"));
        runner.ExecuteTask.IsCompletedSuccessfully.Should().BeTrue("one-shot: nothing stays hosted");
    }

    [Fact]
    public async Task Runner_Force_RunsRecordedOnesToo()
    {
        var done = new FakeBackfill("done", 0);
        await BackfillPass.RunAsync(ScopeFactory, [done.Definition], new ListLogger(), default);
        var runner = new BackfillRunner(ScopeFactory, [done.Definition], StartupBackfillMode.Force, new ListLogger());

        await runner.StartAsync(CancellationToken.None);
        await runner.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));

        done.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Runner_UnreachableDatabase_Logs_InsteadOfStoppingTheHost()
    {
        await using var unreachable = new ServiceCollection()
            .AddDbContext<TempoDbContext>(o => o.UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1"))
            .BuildServiceProvider();
        var logger = new ListLogger();
        var runner = new BackfillRunner(
            unreachable.GetRequiredService<IServiceScopeFactory>(),
            [new FakeBackfill("any", 0).Definition],
            StartupBackfillMode.Run,
            logger);

        await runner.StartAsync(CancellationToken.None);
        await runner.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(15));

        // A faulted ExecuteTask is what makes .NET stop the host
        runner.ExecuteTask.IsFaulted.Should().BeFalse();
        logger.Messages.Should().Contain(m => m.StartsWith("Startup backfills could not run"));
    }

    // --- backfill command ---

    [Theory]
    [InlineData(new[] { "backfill" }, false, false)]
    [InlineData(new[] { "backfill", "--force" }, true, false)]
    [InlineData(new[] { "backfill", "-h" }, false, true)]
    public void Command_Parse(string[] args, bool force, bool help)
    {
        BackfillCommand.TryParse(args, out var parsed, out var error).Should().BeTrue();
        error.Should().BeNull();
        parsed.Should().Be(new BackfillArgs(force, help));
    }

    [Fact]
    public void Command_Parse_UnknownOption_Fails()
    {
        BackfillCommand.TryParse(["backfill", "--all"], out _, out var error).Should().BeFalse();
        error.Should().Contain("--all");
    }

    [Fact]
    public async Task Command_RunsPending_ExitsZero_ThenNothingToDo()
    {
        var backfill = new FakeBackfill("cadence", [4, 0]);

        (await BackfillCommand.ExecuteAsync(ScopeFactory, [backfill.Definition], force: false, new ListLogger())).Should().Be(0);
        backfill.Calls.Should().Be(2);

        var logger = new ListLogger();
        (await BackfillCommand.ExecuteAsync(ScopeFactory, [backfill.Definition], force: false, logger)).Should().Be(0);
        backfill.Calls.Should().Be(2);
        logger.Messages.Should().Contain(m => m.Contains("already complete; nothing to run"));

        (await BackfillCommand.ExecuteAsync(ScopeFactory, [backfill.Definition], force: true, new ListLogger())).Should().Be(0);
        backfill.Calls.Should().Be(3);
    }

    [Fact]
    public async Task Command_SomethingNotRecorded_ExitsOne()
    {
        var broken = new FakeBackfill("broken", 0) { Throws = true };

        (await BackfillCommand.ExecuteAsync(ScopeFactory, [broken.Definition], force: false, new ListLogger())).Should().Be(1);
    }

    [Fact]
    public void All_HasSevenUniquelyNamedBackfills()
    {
        StartupBackfills.All.Should().HaveCount(7);
        StartupBackfills.All.Select(b => b.Name).Should().OnlyHaveUniqueItems();
    }

    private IServiceScopeFactory ScopeFactory => _provider.GetRequiredService<IServiceScopeFactory>();

    private async Task<List<string>> CompletedNamesAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        return await db.CompletedBackfills.OrderBy(c => c.Name).Select(c => c.Name).ToListAsync();
    }

    /// <summary>Returns <paramref name="processedPerCall"/> in order; the last value repeats.</summary>
    private sealed class FakeBackfill(string name, params int[] processedPerCall)
    {
        private int _calls;

        public FakeBackfill(string name, int processed, int version) : this(name, processed)
        {
            Version = version;
        }

        public int Version { get; } = 1;
        public bool Throws { get; init; }
        public int Calls => Volatile.Read(ref _calls);

        public BackfillDefinition Definition => new(name, Version, (_, _) =>
        {
            var call = Interlocked.Increment(ref _calls) - 1;
            return Throws
                ? throw new InvalidOperationException("boom")
                : Task.FromResult(processedPerCall[Math.Min(call, processedPerCall.Length - 1)]);
        });
    }

    private sealed class ListLogger : ILogger<BackfillRunner>
    {
        private readonly object _gate = new();
        private readonly List<(LogLevel Level, string Message)> _entries = new();

        public List<string> Messages
        {
            get { lock (_gate) { return _entries.Select(e => e.Message).ToList(); } }
        }

        public List<string> Warnings
        {
            get { lock (_gate) { return _entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToList(); } }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_gate) { _entries.Add((logLevel, formatter(state, exception))); }
        }
    }
}
