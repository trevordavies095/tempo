using FluentAssertions;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class PasswordPolicyTests
{
    [Fact]
    public void TryValidate_ValidPassphrase_ReturnsTrue()
    {
        PasswordPolicy.TryValidate("Correct horse battery staple", "alice", out var err).Should().BeTrue();
        err.Should().BeNull();
    }

    [Fact]
    public void TryValidate_ValidMatchesTestDefault_ReturnsTrue()
    {
        PasswordPolicy.TryValidate(TestPasswords.Default, "u1", out var err).Should().BeTrue();
        err.Should().BeNull();
    }

    [Fact]
    public void TryValidate_Empty_ReturnsFalse()
    {
        PasswordPolicy.TryValidate("", "user", out var err).Should().BeFalse();
        err.Should().Be("Password is required");
    }

    [Fact]
    public void TryValidate_WhitespaceOnly_ReturnsFalse()
    {
        PasswordPolicy.TryValidate("   ", "user", out var err).Should().BeFalse();
        err.Should().Be("Password is required");
    }

    [Fact]
    public void TryValidate_TooShort_ReturnsFalse()
    {
        PasswordPolicy.TryValidate("abcdefghijklmno", "user", out var err).Should().BeFalse();
        err.Should().Contain("16");
    }

    [Fact]
    public void TryValidate_TooLong_ReturnsFalse()
    {
        PasswordPolicy.TryValidate(new string('x', 65), "user", out var err).Should().BeFalse();
        err.Should().Contain("64");
    }

    [Fact]
    public void TryValidate_Utf8TooManyBytes_ReturnsFalse()
    {
        var password = new string('\u00e9', 40);
        password.Length.Should().Be(40);
        PasswordPolicy.TryValidate(password, "user", out var err).Should().BeFalse();
        err.Should().Contain("UTF-8");
    }

    [Fact]
    public void TryValidate_FiveRepeatedChars_ReturnsFalse()
    {
        PasswordPolicy.TryValidate("abcdefghijklllll", "user", out var err).Should().BeFalse();
        err.Should().Contain("repeated");
    }

    [Fact]
    public void TryValidate_UsernameSubstring_ReturnsFalse()
    {
        PasswordPolicy.TryValidate("prefix-myuser-extra-stuff", "myuser", out var err).Should().BeFalse();
        err.Should().Contain("username");
    }

    [Fact]
    public void TryValidate_ShortUsername_SkipsSubstringCheck()
    {
        PasswordPolicy.TryValidate("ab-prefix-cd-extra!", "xy", out var err).Should().BeTrue();
        err.Should().BeNull();
    }

    [Fact]
    public void TryValidate_Blocklist_ReturnsFalse()
    {
        PasswordPolicy.TryValidate("passwordpassword", "user", out var err).Should().BeFalse();
        err.Should().Contain("common");
    }

    [Fact]
    public void TryValidate_FourRepeatedChars_Allowed()
    {
        PasswordPolicy.TryValidate("abcdefghijklmnoooo", "user", out var err).Should().BeTrue();
        err.Should().BeNull();
    }
}
