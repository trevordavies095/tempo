using FluentAssertions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Tempo.Api.Logging;
using Xunit;

namespace Tempo.Api.Tests.Logging;

public class LoggingProfileTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Parse_NullOrWhitespace_ReturnsStandard(string? value)
    {
        LoggingProfile.Parse(value).Should().Be(LoggingProfileKind.Standard);
    }

    [Theory]
    [InlineData("standard")]
    [InlineData("Standard")]
    [InlineData("STANDARD")]
    [InlineData(" standard ")]
    public void Parse_Standard_IsCaseInsensitive(string value)
    {
        LoggingProfile.Parse(value).Should().Be(LoggingProfileKind.Standard);
    }

    [Theory]
    [InlineData("debug")]
    [InlineData("Debug")]
    [InlineData("DEBUG")]
    [InlineData(" debug ")]
    public void Parse_Debug_IsCaseInsensitive(string value)
    {
        LoggingProfile.Parse(value).Should().Be(LoggingProfileKind.Debug);
    }

    [Theory]
    [InlineData("verbose")]
    [InlineData("debugging")]
    [InlineData("trace")]
    [InlineData("info")]
    public void Parse_Unknown_ThrowsWithAllowedValues(string value)
    {
        var act = () => LoggingProfile.Parse(value);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*standard*")
            .WithMessage("*debug*")
            .WithMessage($"*{value}*");
    }

    [Fact]
    public void ToConfigName_ReturnsLowercaseNames()
    {
        LoggingProfile.ToConfigName(LoggingProfileKind.Standard).Should().Be("standard");
        LoggingProfile.ToConfigName(LoggingProfileKind.Debug).Should().Be("debug");
    }

    [Fact]
    public void ApplyLevels_Standard_DropsEfAndSystemInformation_KeepsTempoAndLifetime()
    {
        var sink = new CollectingSink();
        using var logger = CreateLogger(LoggingProfileKind.Standard, sink);

        logger.ForContext(Constants.SourceContextPropertyName, "Microsoft.EntityFrameworkCore.Database.Command")
            .Information("Executed DbCommand");
        logger.ForContext(Constants.SourceContextPropertyName, "System")
            .Information("System chatter");
        logger.ForContext(Constants.SourceContextPropertyName, "Tempo.Api")
            .Information("Import job progress");
        logger.ForContext(Constants.SourceContextPropertyName, "Microsoft.Hosting.Lifetime")
            .Information("Application started");
        logger.ForContext(Constants.SourceContextPropertyName, "Tempo.Api")
            .Debug("debug-level event");

        sink.Events.Should().ContainSingle(e => e.MessageTemplate.Text == "Import job progress");
        sink.Events.Should().ContainSingle(e => e.MessageTemplate.Text == "Application started");
        sink.Events.Should().NotContain(e => e.MessageTemplate.Text == "Executed DbCommand");
        sink.Events.Should().NotContain(e => e.MessageTemplate.Text == "System chatter");
        sink.Events.Should().NotContain(e => e.MessageTemplate.Text == "debug-level event");
    }

    [Fact]
    public void ApplyLevels_Debug_EmitsEfInformation_ButNotSerilogDebug()
    {
        var sink = new CollectingSink();
        using var logger = CreateLogger(LoggingProfileKind.Debug, sink);

        logger.ForContext(Constants.SourceContextPropertyName, "Microsoft.EntityFrameworkCore.Database.Command")
            .Information("Executed DbCommand");
        logger.ForContext(Constants.SourceContextPropertyName, "Tempo.Api")
            .Information("Import job progress");
        logger.ForContext(Constants.SourceContextPropertyName, "Tempo.Api")
            .Debug("debug-level event");

        sink.Events.Should().ContainSingle(e => e.MessageTemplate.Text == "Executed DbCommand");
        sink.Events.Should().ContainSingle(e => e.MessageTemplate.Text == "Import job progress");
        sink.Events.Should().NotContain(e => e.MessageTemplate.Text == "debug-level event");
    }

    [Theory]
    [InlineData(LoggingProfileKind.Standard, "/workouts", 200, LogEventLevel.Verbose)]
    [InlineData(LoggingProfileKind.Standard, "/auth/me", 401, LogEventLevel.Verbose)]
    [InlineData(LoggingProfileKind.Standard, "/workouts", 500, LogEventLevel.Error)]
    [InlineData(LoggingProfileKind.Debug, "/workouts", 200, LogEventLevel.Information)]
    [InlineData(LoggingProfileKind.Debug, "/auth/me", 401, LogEventLevel.Information)]
    [InlineData(LoggingProfileKind.Standard, "/health", 200, LogEventLevel.Verbose)]
    [InlineData(LoggingProfileKind.Debug, "/health", 200, LogEventLevel.Verbose)]
    [InlineData(LoggingProfileKind.Standard, "/ready", 200, LogEventLevel.Verbose)]
    [InlineData(LoggingProfileKind.Debug, "/ready", 200, LogEventLevel.Verbose)]
    [InlineData(LoggingProfileKind.Standard, "/health", 500, LogEventLevel.Error)]
    [InlineData(LoggingProfileKind.Debug, "/ready", 500, LogEventLevel.Error)]
    [InlineData(LoggingProfileKind.Standard, "/HEALTH", 200, LogEventLevel.Verbose)]
    [InlineData(LoggingProfileKind.Debug, "/workouts/healthkit-uuids", 200, LogEventLevel.Information)]
    public void GetRequestLogLevel_MatchesPolicy(
        LoggingProfileKind profile,
        string path,
        int statusCode,
        LogEventLevel expected)
    {
        LoggingProfile.GetRequestLogLevel(profile, path, statusCode, exception: null)
            .Should().Be(expected);
    }

    [Fact]
    public void GetRequestLogLevel_WithException_IsErrorOnBothProfiles()
    {
        var ex = new InvalidOperationException("boom");

        LoggingProfile.GetRequestLogLevel(LoggingProfileKind.Standard, "/workouts", 200, ex)
            .Should().Be(LogEventLevel.Error);
        LoggingProfile.GetRequestLogLevel(LoggingProfileKind.Debug, "/workouts", 200, ex)
            .Should().Be(LogEventLevel.Error);
    }

    private static Logger CreateLogger(LoggingProfileKind profile, CollectingSink sink) =>
        new LoggerConfiguration()
            .ApplyLevels(profile)
            .WriteTo.Sink(sink)
            .CreateLogger();

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
