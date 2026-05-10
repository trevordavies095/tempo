using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Tempo.Api.Authentication;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

/// <summary>
/// Unit tests for JwtService covering token generation, validation, and configuration edge cases
/// </summary>
public class JwtServiceTests
{
    private readonly Mock<ILogger<JwtService>> _loggerMock;
    private readonly IConfiguration _configuration;

    public JwtServiceTests()
    {
        _loggerMock = new Mock<ILogger<JwtService>>();
        
        // Create configuration with JWT settings
        var configDict = new Dictionary<string, string?>
        {
            { "JWT:SecretKey", "test-secret-key-that-is-at-least-32-characters-long" },
            { "JWT:Issuer", "TestIssuer" },
            { "JWT:Audience", "TestAudience" },
            { "JWT:ExpirationDays", "7" }
        };
        
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidConfiguration_CreatesService()
    {
        // Act
        var service = new JwtService(_configuration, _loggerMock.Object);

        // Assert
        service.Should().NotBeNull();
        service.ExpirationDays.Should().Be(7);
    }

    [Fact]
    public void Constructor_WithMissingSecretKey_ThrowsInvalidOperationException()
    {
        // Arrange
        var configDict = new Dictionary<string, string?>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => 
            new JwtService(config, _loggerMock.Object));
        exception.Message.Should().Contain("JWT:SecretKey");
    }

    [Fact]
    public void Constructor_WithDefaultIssuer_UsesDefaultValue()
    {
        // Arrange
        var configDict = new Dictionary<string, string?>
        {
            { "JWT:SecretKey", "test-secret-key-that-is-at-least-32-characters-long" }
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        // Act
        var service = new JwtService(config, _loggerMock.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithDefaultAudience_UsesDefaultValue()
    {
        // Arrange
        var configDict = new Dictionary<string, string?>
        {
            { "JWT:SecretKey", "test-secret-key-that-is-at-least-32-characters-long" }
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        // Act
        var service = new JwtService(config, _loggerMock.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithCustomExpirationDays_UsesCustomValue()
    {
        // Arrange
        var configDict = new Dictionary<string, string?>
        {
            { "JWT:SecretKey", "test-secret-key-that-is-at-least-32-characters-long" },
            { "JWT:ExpirationDays", "14" }
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        // Act
        var service = new JwtService(config, _loggerMock.Object);

        // Assert
        service.ExpirationDays.Should().Be(14);
    }

    [Fact]
    public void Constructor_WithMissingExpirationDays_UsesDefaultValue()
    {
        // Arrange
        var configDict = new Dictionary<string, string?>
        {
            { "JWT:SecretKey", "test-secret-key-that-is-at-least-32-characters-long" }
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        // Act
        var service = new JwtService(config, _loggerMock.Object);

        // Assert
        service.ExpirationDays.Should().Be(7); // Default value
    }

    #endregion

    #region GenerateToken Tests

    [Fact]
    public void GenerateToken_WithValidUser_ReturnsToken()
    {
        // Arrange
        var service = new JwtService(_configuration, _loggerMock.Object);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "testuser"
        };

        // Act
        var token = service.GenerateToken(user);

        // Assert
        token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateToken_WithValidUser_ContainsCorrectClaims()
    {
        // Arrange
        var service = new JwtService(_configuration, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Username = "testuser"
        };

        // Act
        var token = service.GenerateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var jsonToken = handler.ReadJwtToken(token);

        // Assert
        jsonToken.Claims.Should().Contain(c => c.Type == ClaimTypes.NameIdentifier && c.Value == userId.ToString());
        jsonToken.Claims.Should().Contain(c => c.Type == ClaimTypes.Name && c.Value == "testuser");
        jsonToken.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Jti);
        jsonToken.Claims.Should().Contain(c => c.Type == TempoJwtClaimTypes.SessionVersion && c.Value == "0");
        jsonToken.Claims.Should().Contain(c => c.Type == TempoJwtClaimTypes.RememberMe && c.Value == "false");
    }

    [Fact]
    public void GenerateToken_EmbedsSessionVersionFromUser()
    {
        var service = new JwtService(_configuration, _loggerMock.Object);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "u",
            SessionVersion = 3
        };

        var token = service.GenerateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var jsonToken = handler.ReadJwtToken(token);

        jsonToken.Claims.Should().Contain(c => c.Type == TempoJwtClaimTypes.SessionVersion && c.Value == "3");
    }

    [Fact]
    public void GenerateToken_WithRememberMe_SetsRememberMeClaimTrue()
    {
        var service = new JwtService(_configuration, _loggerMock.Object);
        var user = new User { Id = Guid.NewGuid(), Username = "u" };

        var token = service.GenerateToken(user, rememberMe: true);
        var handler = new JwtSecurityTokenHandler();
        var jsonToken = handler.ReadJwtToken(token);

        jsonToken.Claims.Should().Contain(c => c.Type == TempoJwtClaimTypes.RememberMe && c.Value == "true");
    }

    [Fact]
    public void GenerateToken_WithValidUser_SetsCorrectIssuer()
    {
        // Arrange
        var service = new JwtService(_configuration, _loggerMock.Object);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "testuser"
        };

        // Act
        var token = service.GenerateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var jsonToken = handler.ReadJwtToken(token);

        // Assert
        jsonToken.Issuer.Should().Be("TestIssuer");
    }

    [Fact]
    public void GenerateToken_WithValidUser_SetsCorrectAudience()
    {
        // Arrange
        var service = new JwtService(_configuration, _loggerMock.Object);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "testuser"
        };

        // Act
        var token = service.GenerateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var jsonToken = handler.ReadJwtToken(token);

        // Assert
        jsonToken.Audiences.Should().Contain("TestAudience");
    }

    [Fact]
    public void GenerateToken_WithValidUser_SetsCorrectExpiration()
    {
        // Arrange
        var service = new JwtService(_configuration, _loggerMock.Object);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "testuser"
        };

        // Act
        var token = service.GenerateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var jsonToken = handler.ReadJwtToken(token);

        // Assert
        jsonToken.ValidTo.Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void GenerateToken_WithValidUser_GeneratesUniqueJti()
    {
        // Arrange
        var service = new JwtService(_configuration, _loggerMock.Object);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "testuser"
        };

        // Act
        var token1 = service.GenerateToken(user);
        var token2 = service.GenerateToken(user);
        
        var handler = new JwtSecurityTokenHandler();
        var jsonToken1 = handler.ReadJwtToken(token1);
        var jsonToken2 = handler.ReadJwtToken(token2);

        // Assert
        var jti1 = jsonToken1.Claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value;
        var jti2 = jsonToken2.Claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value;
        jti1.Should().NotBe(jti2);
    }

    #endregion

    #region ValidateToken Tests

    [Fact]
    public void ValidateToken_WithValidToken_ReturnsClaimsPrincipal()
    {
        // Arrange
        var service = new JwtService(_configuration, _loggerMock.Object);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "testuser"
        };
        var token = service.GenerateToken(user);

        // Act
        var principal = service.ValidateToken(token);

        // Assert
        principal.Should().NotBeNull();
        principal!.Identity.Should().NotBeNull();
        principal.Identity!.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void ValidateToken_WithValidToken_ContainsCorrectClaims()
    {
        // Arrange
        var service = new JwtService(_configuration, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Username = "testuser"
        };
        var token = service.GenerateToken(user);

        // Act
        var principal = service.ValidateToken(token);

        // Assert
        principal.Should().NotBeNull();
        principal!.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be(userId.ToString());
        principal.FindFirst(ClaimTypes.Name)!.Value.Should().Be("testuser");
    }

    [Fact]
    public void ValidateToken_WithExpiredToken_ReturnsNull()
    {
        // Arrange
        var configDict = new Dictionary<string, string?>
        {
            { "JWT:SecretKey", "test-secret-key-that-is-at-least-32-characters-long" },
            { "JWT:Issuer", "TestIssuer" },
            { "JWT:Audience", "TestAudience" },
            { "JWT:ExpirationDays", "7" }
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();
        
        var service = new JwtService(config, _loggerMock.Object);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "testuser"
        };
        
        // Create a token that expired 1 day ago (well beyond the 5-minute ClockSkew tolerance)
        // This ensures the token is genuinely expired and will be rejected by ValidateToken
        var token = service.GenerateToken(user, expirationDays: -1);

        // Act
        var principal = service.ValidateToken(token);

        // Assert
        principal.Should().BeNull();
    }

    [Fact]
    public void ValidateToken_WithInvalidSignature_ReturnsNull()
    {
        // Arrange
        var service = new JwtService(_configuration, _loggerMock.Object);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "testuser"
        };
        var token = service.GenerateToken(user);

        // Modify token to have invalid signature
        var invalidToken = token.Substring(0, token.Length - 5) + "xxxxx";

        // Act
        var principal = service.ValidateToken(invalidToken);

        // Assert
        principal.Should().BeNull();
    }

    [Fact]
    public void ValidateToken_WithWrongIssuer_ReturnsNull()
    {
        // Arrange
        var service1 = new JwtService(_configuration, _loggerMock.Object);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "testuser"
        };
        var token = service1.GenerateToken(user);

        // Create service with different issuer
        var configDict = new Dictionary<string, string?>
        {
            { "JWT:SecretKey", "test-secret-key-that-is-at-least-32-characters-long" },
            { "JWT:Issuer", "DifferentIssuer" },
            { "JWT:Audience", "TestAudience" }
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();
        var service2 = new JwtService(config, _loggerMock.Object);

        // Act
        var principal = service2.ValidateToken(token);

        // Assert
        principal.Should().BeNull();
    }

    [Fact]
    public void ValidateToken_WithWrongAudience_ReturnsNull()
    {
        // Arrange
        var service1 = new JwtService(_configuration, _loggerMock.Object);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "testuser"
        };
        var token = service1.GenerateToken(user);

        // Create service with different audience
        var configDict = new Dictionary<string, string?>
        {
            { "JWT:SecretKey", "test-secret-key-that-is-at-least-32-characters-long" },
            { "JWT:Issuer", "TestIssuer" },
            { "JWT:Audience", "DifferentAudience" }
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();
        var service2 = new JwtService(config, _loggerMock.Object);

        // Act
        var principal = service2.ValidateToken(token);

        // Assert
        principal.Should().BeNull();
    }

    [Fact]
    public void ValidateToken_WithMalformedToken_ReturnsNull()
    {
        // Arrange
        var service = new JwtService(_configuration, _loggerMock.Object);
        var malformedToken = "not.a.valid.token";

        // Act
        var principal = service.ValidateToken(malformedToken);

        // Assert
        principal.Should().BeNull();
    }

    [Fact]
    public void ValidateToken_WithEmptyToken_ReturnsNull()
    {
        // Arrange
        var service = new JwtService(_configuration, _loggerMock.Object);

        // Act
        var principal = service.ValidateToken("");

        // Assert
        principal.Should().BeNull();
    }

    [Fact]
    public void ValidateToken_WithInvalidToken_LogsWarning()
    {
        // Arrange
        var service = new JwtService(_configuration, _loggerMock.Object);
        var invalidToken = "invalid.token.here";

        // Act
        service.ValidateToken(invalidToken);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    #endregion
}

