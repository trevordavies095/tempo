using FluentAssertions;

namespace Tempo.Api.Tests;

public class ExampleTests
{
    [Fact]
    public void TestInfrastructure_ShouldWork()
    {
        // Arrange
        var value = 42;

        // Act & Assert
        value.Should().Be(42);
    }

    [Fact]
    public void FluentAssertions_ShouldBeAvailable()
    {
        // Arrange
        var list = new List<int> { 1, 2, 3 };

        // Act & Assert
        list.Should().NotBeNull()
            .And.HaveCount(3)
            .And.Contain(2);
    }
}

