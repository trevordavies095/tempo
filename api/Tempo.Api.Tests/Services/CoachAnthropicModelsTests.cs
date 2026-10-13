using FluentAssertions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class CoachAnthropicModelsTests
{
    [Fact]
    public void NormalizeOrReject_NullOrEmpty_ReturnsDefault()
    {
        CoachAnthropicModels.NormalizeOrReject(null).Should().Be(CoachAnthropicModels.DefaultModelId);
        CoachAnthropicModels.NormalizeOrReject("").Should().Be(CoachAnthropicModels.DefaultModelId);
        CoachAnthropicModels.NormalizeOrReject("   ").Should().Be(CoachAnthropicModels.DefaultModelId);
    }

    [Fact]
    public void NormalizeOrReject_KnownModels_ReturnsTrimmed()
    {
        CoachAnthropicModels.NormalizeOrReject("claude-opus-4-7")
            .Should().Be("claude-opus-4-7");
        CoachAnthropicModels.NormalizeOrReject("  claude-sonnet-4-6  ")
            .Should().Be("claude-sonnet-4-6");
    }

    [Fact]
    public void NormalizeOrReject_Unknown_ReturnsNull()
    {
        CoachAnthropicModels.NormalizeOrReject("gpt-4o").Should().BeNull();
        CoachAnthropicModels.NormalizeOrReject("claude-haiku-4-5").Should().BeNull();
    }
}
