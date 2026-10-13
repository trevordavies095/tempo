using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class CoachAnthropicSecretProtectorTests
{
    private static CoachAnthropicSecretProtector CreateProtector(string secret)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JWT:SecretKey"] = secret
            })
            .Build();
        return new CoachAnthropicSecretProtector(config);
    }

    [Fact]
    public void Constructor_WithMissingSecretKey_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new CoachAnthropicSecretProtector(config));
        exception.Message.Should().Contain("JWT:SecretKey");
    }

    [Fact]
    public void Encrypt_ThenDecrypt_ReturnsPlaintext()
    {
        var protector = CreateProtector("test-secret-key-that-is-at-least-32-characters-long");

        var blob = protector.Encrypt("sk-ant-test-key");
        var roundTrip = protector.Decrypt(blob);

        roundTrip.Should().Be("sk-ant-test-key");
        blob.Should().NotBeEmpty();
        System.Text.Encoding.UTF8.GetString(blob).Should().NotContain("sk-ant-test-key");
    }

    [Fact]
    public void Decrypt_WithDifferentSecret_Fails()
    {
        var first = CreateProtector("test-secret-key-that-is-at-least-32-characters-long");
        var second = CreateProtector("other-secret-key-that-is-at-least-32-characters");

        var blob = first.Encrypt("sk-ant-test-key");

        Assert.ThrowsAny<CryptographicException>(() => second.Decrypt(blob));
    }

    [Fact]
    public void Purpose_IsDistinctFromIntervalsIcu()
    {
        CoachAnthropicSecretProtector.Purpose.Should().Be("tempo-coach-anthropic-api-key");
        CoachAnthropicSecretProtector.Purpose.Should().NotBe(IntervalsIcuSecretProtector.Purpose);
    }
}
