using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class StartupBackfillsTests
{
    private static readonly Type[] StartupOnlyWorkers =
    [
        typeof(RoutePreviewBackfillWorker),
        typeof(SplitHeartRateBackfillWorker),
        typeof(CadenceBackfillWorker),
        typeof(TimerTimeBackfillWorker),
        typeof(DeviceLapBackfillWorker),
        typeof(ZoneSecondsBackfillWorker),
    ];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("run")]
    [InlineData("RUN")]
    [InlineData(" Run ")]
    public void Parse_DefaultsAndRun_ReturnRun(string? value)
    {
        StartupBackfills.Parse(value).Should().Be(StartupBackfillMode.Run);
    }

    [Theory]
    [InlineData("skip")]
    [InlineData("SKIP")]
    [InlineData(" skip ")]
    public void Parse_Skip_IsCaseInsensitive(string value)
    {
        StartupBackfills.Parse(value).Should().Be(StartupBackfillMode.Skip);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("false")]
    [InlineData("skipp")]
    public void Parse_Unknown_Throws(string value)
    {
        var act = () => StartupBackfills.Parse(value);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Tempo:Backfills:OnStartup*run, skip*");
    }

    [Fact]
    public void DescribeStartup_Run_NamesEveryBackfill()
    {
        var line = StartupBackfills.DescribeStartup(StartupBackfillMode.Run);

        line.Should().StartWith("Startup backfills: run");
        foreach (var name in StartupBackfills.Names)
        {
            line.Should().Contain(name);
        }
    }

    [Fact]
    public void DescribeStartup_Skip_SaysSoAndKeepsRestorePath()
    {
        StartupBackfills.DescribeStartup(StartupBackfillMode.Skip)
            .Should().StartWith("Startup backfills: skip")
            .And.Contain("restore");
    }

    [Fact]
    public void AddStartupBackfills_Run_RegistersAllSevenWorkers()
    {
        var registered = HostedServiceTypes(StartupBackfillMode.Run);

        registered.Should().Contain(StartupOnlyWorkers);
        registered.Should().Contain(typeof(BestEffortBackfillWorker));
        registered.Should().HaveCount(7);
    }

    [Fact]
    public void AddStartupBackfills_Skip_RegistersOnlyRestoreTriggeredBestEffortWorker()
    {
        HostedServiceTypes(StartupBackfillMode.Skip)
            .Should().ContainSingle().Which.Should().Be(typeof(BestEffortBackfillWorker));
    }

    [Fact]
    public async Task BestEffortWorker_Skip_SkipsStartupPass_ButRunsOnRestoreSignal()
    {
        // BestEffortBackfillService is deliberately not registered: any pass that runs fails to
        // resolve it and logs "Best effort backfill failed", which makes passes observable.
        var (worker, signal, logger) = BuildBestEffortWorker(StartupBackfillMode.Skip);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        logger.Errors.Should().BeEmpty("skip must not run the startup pass");

        signal.Request();
        await WaitForAsync(() => logger.Errors.Count > 0);
        logger.Errors.Should().ContainSingle("a restore signal still runs a pass under skip");

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task BestEffortWorker_Run_RunsStartupPass()
    {
        var (worker, _, logger) = BuildBestEffortWorker(StartupBackfillMode.Run);

        await worker.StartAsync(CancellationToken.None);
        await WaitForAsync(() => logger.Errors.Count > 0);
        logger.Errors.Should().ContainSingle();

        await worker.StopAsync(CancellationToken.None);
    }

    private static List<Type> HostedServiceTypes(StartupBackfillMode mode)
    {
        var services = new ServiceCollection();
        services.AddStartupBackfills(mode);
        return services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType!)
            .ToList();
    }

    private static (BestEffortBackfillWorker Worker, BestEffortBackfillSignal Signal, ErrorLogger Logger)
        BuildBestEffortWorker(StartupBackfillMode mode)
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var signal = new BestEffortBackfillSignal();
        var logger = new ErrorLogger();
        var worker = new BestEffortBackfillWorker(
            provider.GetRequiredService<IServiceScopeFactory>(), signal, mode, logger);
        return (worker, signal, logger);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(20);
        }
    }

    private sealed class ErrorLogger : ILogger<BestEffortBackfillWorker>
    {
        private readonly object _gate = new();
        private readonly List<string> _errors = new();

        public List<string> Errors
        {
            get { lock (_gate) { return _errors.ToList(); } }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                lock (_gate) { _errors.Add(formatter(state, exception)); }
            }
        }
    }
}
