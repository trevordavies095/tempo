using FluentAssertions;
using Tempo.Api.Models;
using Xunit;

namespace Tempo.Api.Tests;

public class VersionInfoTests
{
    [Fact]
    public void FormatSnapshot_UsesFrozenKeyOrderAndTrailingNewline()
    {
        var text = VersionInfo.FormatSnapshot(
            version: "2.9.0",
            gitCommit: "abc123",
            buildDate: "2026-09-16T00:00:00Z",
            logProfile: "debug",
            environment: "Production",
            apiImage: "ghcr.io/example/api:stable",
            frontendImage: "ghcr.io/example/frontend:edge");

        text.Should().Be(
            "Tempo support snapshot\n" +
            "\n" +
            "version: 2.9.0\n" +
            "gitCommit: abc123\n" +
            "buildDate: 2026-09-16T00:00:00Z\n" +
            "logProfile: debug\n" +
            "environment: Production\n" +
            "apiImage: ghcr.io/example/api:stable\n" +
            "frontendImage: ghcr.io/example/frontend:edge\n");
    }

    [Fact]
    public void ApplySnapshot_ProjectsPublicFieldsOnly()
    {
        var info = new VersionInfo
        {
            Version = "1.0.0",
            GitCommit = "deadbeef",
            BuildDate = "unknown",
            LogProfile = "standard",
            Environment = "Testing",
            ApiImage = "unknown",
            FrontendImage = "local build",
        };

        info.ApplySnapshot();

        info.Snapshot.Should().Be(VersionInfo.FormatSnapshot(
            info.Version,
            info.GitCommit,
            info.BuildDate,
            info.LogProfile,
            info.Environment,
            info.ApiImage,
            info.FrontendImage));
        info.Snapshot.Should().Contain("logProfile: standard");
        info.Snapshot.Should().Contain("frontendImage: local build");
    }
}
