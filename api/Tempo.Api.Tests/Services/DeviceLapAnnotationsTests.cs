using FluentAssertions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class DeviceLapAnnotationsTests
{
    [Theory]
    [InlineData("Manual", "manual")]
    [InlineData("PositionStart", "position_start")]
    [InlineData("SessionEnd", "session_end")]
    [InlineData("FitnessEquipment", "fitness_equipment")]
    [InlineData("Warmup", "warmup")]
    [InlineData("Cooldown", "cooldown")]
    public void Normalize_PascalCase_ReturnsSnakeCase(string raw, string expected)
    {
        DeviceLapAnnotations.Normalize(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("position_start")]
    [InlineData("rest")]
    public void Normalize_AlreadySnakeCase_IsUnchanged(string raw)
    {
        DeviceLapAnnotations.Normalize(raw).Should().Be(raw);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Invalid")]
    [InlineData("invalid")]
    public void Normalize_InvalidOrAbsent_ReturnsNull(string? raw)
    {
        DeviceLapAnnotations.Normalize(raw).Should().BeNull();
    }

    [Fact]
    public void Normalize_UnknownFutureValue_ReturnsSnakeCasedName()
    {
        DeviceLapAnnotations.Normalize("StrideSegment").Should().Be("stride_segment");
    }
}
