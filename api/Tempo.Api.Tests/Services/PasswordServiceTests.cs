using FluentAssertions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

/// <summary>
/// Unit tests for PasswordService covering password hashing, verification, and edge cases
/// </summary>
public class PasswordServiceTests
{
    private readonly PasswordService _service;

    public PasswordServiceTests()
    {
        _service = new PasswordService();
    }

    #region HashPassword Tests

    [Fact]
    public void HashPassword_WithValidPassword_ReturnsHash()
    {
        // Arrange
        var password = "TestPassword123!";

        // Act
        var hash = _service.HashPassword(password);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().NotBe(password);
        hash.Should().StartWith("$2"); // BCrypt hash starts with $2
    }

    [Fact]
    public void HashPassword_WithSamePassword_GeneratesDifferentHashes()
    {
        // Arrange
        var password = "TestPassword123!";

        // Act
        var hash1 = _service.HashPassword(password);
        var hash2 = _service.HashPassword(password);

        // Assert
        hash1.Should().NotBe(hash2); // BCrypt includes salt, so hashes differ
    }

    [Fact]
    public void HashPassword_WithDifferentPasswords_GeneratesDifferentHashes()
    {
        // Arrange
        var password1 = "TestPassword123!";
        var password2 = "DifferentPassword456!";

        // Act
        var hash1 = _service.HashPassword(password1);
        var hash2 = _service.HashPassword(password2);

        // Assert
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void HashPassword_WithLongPassword_ReturnsHash()
    {
        // Arrange
        var password = new string('a', 1000); // Very long password

        // Act
        var hash = _service.HashPassword(password);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().StartWith("$2");
    }

    [Fact]
    public void HashPassword_WithSpecialCharacters_ReturnsHash()
    {
        // Arrange
        var password = "P@ssw0rd!@#$%^&*()_+-=[]{}|;:,.<>?";

        // Act
        var hash = _service.HashPassword(password);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().StartWith("$2");
    }

    [Fact]
    public void HashPassword_WithUnicodeCharacters_ReturnsHash()
    {
        // Arrange
        var password = "Pässwörd123!";

        // Act
        var hash = _service.HashPassword(password);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().StartWith("$2");
    }

    #endregion

    #region VerifyPassword Tests

    [Fact]
    public void VerifyPassword_WithCorrectPassword_ReturnsTrue()
    {
        // Arrange
        var password = "TestPassword123!";
        var hash = _service.HashPassword(password);

        // Act
        var result = _service.VerifyPassword(password, hash);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_WithIncorrectPassword_ReturnsFalse()
    {
        // Arrange
        var password = "TestPassword123!";
        var wrongPassword = "WrongPassword456!";
        var hash = _service.HashPassword(password);

        // Act
        var result = _service.VerifyPassword(wrongPassword, hash);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_WithCaseSensitivePassword_IsCaseSensitive()
    {
        // Arrange
        var password = "TestPassword123!";
        var wrongCasePassword = "testpassword123!";
        var hash = _service.HashPassword(password);

        // Act
        var result = _service.VerifyPassword(wrongCasePassword, hash);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_WithWhitespaceDifference_ReturnsFalse()
    {
        // Arrange
        var password = "TestPassword123!";
        var passwordWithWhitespace = "TestPassword123! ";
        var hash = _service.HashPassword(password);

        // Act
        var result = _service.VerifyPassword(passwordWithWhitespace, hash);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_WithEmptyPassword_ReturnsFalse()
    {
        // Arrange
        var password = "TestPassword123!";
        var hash = _service.HashPassword(password);

        // Act
        var result = _service.VerifyPassword("", hash);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_WithInvalidHash_ThrowsException()
    {
        // Arrange
        var password = "TestPassword123!";
        var invalidHash = "invalid-hash-format";

        // Act & Assert
        var exception = Assert.ThrowsAny<Exception>(() => 
            _service.VerifyPassword(password, invalidHash));
        exception.Should().NotBeNull();
    }

    [Fact]
    public void VerifyPassword_WithNullHash_ThrowsException()
    {
        // Arrange
        var password = "TestPassword123!";

        // Act & Assert
        var exception = Assert.ThrowsAny<Exception>(() => 
            _service.VerifyPassword(password, null!));
        exception.Should().NotBeNull();
    }

    [Fact]
    public void VerifyPassword_WithNullPassword_ThrowsException()
    {
        // Arrange
        var password = "TestPassword123!";
        var hash = _service.HashPassword(password);

        // Act & Assert
        var exception = Assert.ThrowsAny<Exception>(() => 
            _service.VerifyPassword(null!, hash));
        exception.Should().NotBeNull();
    }

    [Fact]
    public void VerifyPassword_RoundTrip_WorksCorrectly()
    {
        // Arrange
        var password = "TestPassword123!";

        // Act
        var hash = _service.HashPassword(password);
        var verifyResult = _service.VerifyPassword(password, hash);

        // Assert
        verifyResult.Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_WithMultipleHashes_VerifiesCorrectly()
    {
        // Arrange
        var password = "TestPassword123!";
        var hash1 = _service.HashPassword(password);
        var hash2 = _service.HashPassword(password);

        // Act
        var result1 = _service.VerifyPassword(password, hash1);
        var result2 = _service.VerifyPassword(password, hash2);

        // Assert
        result1.Should().BeTrue();
        result2.Should().BeTrue();
        // Both hashes should verify correctly even though they're different
        hash1.Should().NotBe(hash2);
    }

    #endregion

    #region BCrypt Work Factor Tests

    [Fact]
    public void HashPassword_UsesCorrectWorkFactor()
    {
        // Arrange
        var password = "TestPassword123!";

        // Act
        var hash = _service.HashPassword(password);

        // Assert
        // BCrypt hash format: $2a$[cost]$[salt][hash]
        // Work factor 12 means cost should be 12
        var parts = hash.Split('$');
        parts.Length.Should().BeGreaterThan(3);
        if (parts.Length > 3)
        {
            var cost = int.Parse(parts[2]);
            cost.Should().Be(12); // WorkFactor constant is 12
        }
    }

    #endregion
}

