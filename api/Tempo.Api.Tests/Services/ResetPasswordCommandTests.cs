using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tempo.Api.Commands;
using Tempo.Api.Data;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class ResetPasswordCommandParseTests
{
    [Fact]
    public void TryParse_PasswordStdinOnly_AllowsOptionalUsername()
    {
        ResetPasswordCommand.TryParse(
                ["reset-password", "--password-stdin"],
                out var parsed,
                out var error)
            .Should().BeTrue();
        error.Should().BeNull();
        parsed.Should().NotBeNull();
        parsed!.Username.Should().BeNull();
        parsed.PasswordStdin.Should().BeTrue();
        parsed.Help.Should().BeFalse();
    }

    [Fact]
    public void TryParse_UsernameOnly_DoesNotRequirePasswordStdin()
    {
        ResetPasswordCommand.TryParse(
                ["reset-password", "--username", "alice"],
                out var parsed,
                out var error)
            .Should().BeTrue();
        error.Should().BeNull();
        parsed.Should().NotBeNull();
        parsed!.Username.Should().Be("alice");
        parsed.PasswordStdin.Should().BeFalse();
    }

    [Fact]
    public void TryParse_BareVerb_IsValid()
    {
        ResetPasswordCommand.TryParse(["reset-password"], out var parsed, out var error).Should().BeTrue();
        error.Should().BeNull();
        parsed.Should().NotBeNull();
        parsed!.Username.Should().BeNull();
        parsed.PasswordStdin.Should().BeFalse();
        parsed.Help.Should().BeFalse();
    }

    [Fact]
    public void TryParse_Help_LongAndShort()
    {
        ResetPasswordCommand.TryParse(["reset-password", "--help"], out var longHelp, out _).Should().BeTrue();
        longHelp!.Help.Should().BeTrue();

        ResetPasswordCommand.TryParse(["reset-password", "-h"], out var shortHelp, out _).Should().BeTrue();
        shortHelp!.Help.Should().BeTrue();
    }

    [Fact]
    public void TryParse_UnknownFlag_ReturnsError()
    {
        ResetPasswordCommand.TryParse(
                ["reset-password", "--bogus"],
                out var parsed,
                out var error)
            .Should().BeFalse();
        parsed.Should().BeNull();
        error.Should().Contain("Unknown argument");
        error.Should().Contain("--bogus");
    }

    [Fact]
    public void TryParse_ExtraPositional_ReturnsError()
    {
        ResetPasswordCommand.TryParse(
                ["reset-password", "extra"],
                out var parsed,
                out var error)
            .Should().BeFalse();
        parsed.Should().BeNull();
        error.Should().Contain("Unknown argument");
    }

    [Fact]
    public void TryParse_PasswordFlag_ReturnsError()
    {
        ResetPasswordCommand.TryParse(
                ["reset-password", "--username", "alice", "--password", "secret"],
                out var parsed,
                out var error)
            .Should().BeFalse();
        parsed.Should().BeNull();
        error.Should().Contain("--password-stdin");
        error.Should().Contain("--password");
    }

    [Fact]
    public void TryParse_PasswordEqualsForm_ReturnsError()
    {
        ResetPasswordCommand.TryParse(
                ["reset-password", "--username", "alice", "--password=secret"],
                out var parsed,
                out var error)
            .Should().BeFalse();
        parsed.Should().BeNull();
        error.Should().Contain("--password");
    }

    [Fact]
    public void TryParse_ValidFlags_TrimsUsername_AndIgnoresOrder()
    {
        ResetPasswordCommand.TryParse(
                ["reset-password", "--password-stdin", "--username", "  alice  "],
                out var parsed,
                out var error)
            .Should().BeTrue();
        error.Should().BeNull();
        parsed.Should().NotBeNull();
        parsed!.Username.Should().Be("alice");
        parsed.PasswordStdin.Should().BeTrue();
    }

    [Fact]
    public void Usage_ListsVerbAndFlags()
    {
        ResetPasswordCommand.Usage.Should().Contain("reset-password");
        ResetPasswordCommand.Usage.Should().Contain("--username");
        ResetPasswordCommand.Usage.Should().Contain("--password-stdin");
        ResetPasswordCommand.Usage.Should().Contain("--help");
    }

    [Fact]
    public void TryReadPassword_NoTtyWithoutStdin_ReturnsError()
    {
        ResetPasswordCommand.TryParse(["reset-password"], out var args, out _).Should().BeTrue();
        var input = new FakeResetPasswordInput { IsTty = false };
        var stderr = new StringWriter();

        ResetPasswordCommand.TryReadPassword(args!, input, stderr, out var password, out var error)
            .Should().BeFalse();
        password.Should().BeNull();
        error.Should().Contain("-it");
        error.Should().Contain("--password-stdin");
    }

    [Fact]
    public void TryReadPassword_TtyConfirmMismatch_ReturnsError()
    {
        ResetPasswordCommand.TryParse(["reset-password"], out var args, out _).Should().BeTrue();
        var input = new FakeResetPasswordInput { IsTty = true };
        input.Masked.Enqueue("one-password-here!");
        input.Masked.Enqueue("different-password!");
        var stderr = new StringWriter();

        ResetPasswordCommand.TryReadPassword(args!, input, stderr, out var password, out var error)
            .Should().BeFalse();
        password.Should().BeNull();
        error.Should().Contain("did not match");
    }

    [Fact]
    public void TryReadPassword_PasswordStdin_DoesNotTrim()
    {
        ResetPasswordCommand.TryParse(["reset-password", "--password-stdin"], out var args, out _).Should().BeTrue();
        var input = new FakeResetPasswordInput { IsTty = false, Line = "  keep-leading-spaces" };
        var stderr = new StringWriter();

        ResetPasswordCommand.TryReadPassword(args!, input, stderr, out var password, out var error)
            .Should().BeTrue();
        error.Should().BeNull();
        password.Should().Be("  keep-leading-spaces");
    }

    private sealed class FakeResetPasswordInput : IResetPasswordInput
    {
        public bool IsTty { get; init; }
        public string? Line { get; init; }
        public Queue<string> Masked { get; } = new();

        public string? ReadLine() => Line;

        public string ReadMaskedLine() => Masked.Dequeue();
    }
}

public class ResetPasswordCommandTests : IAsyncLifetime
{
    private string _cloneConnectionString = null!;
    private TempoDbContext _db = null!;
    private readonly PasswordService _passwords = new();
    private ListLogger _logger = null!;

    public async Task InitializeAsync()
    {
        _cloneConnectionString = await PostgresTestFixture.CreateCloneAsync();
        _db = PostgresTestFixture.CreateContext(_cloneConnectionString);
        _logger = new ListLogger();
    }

    public async Task DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        if (_cloneConnectionString is not null)
        {
            await PostgresTestFixture.DropCloneAsync(_cloneConnectionString);
        }
    }

    [Fact]
    public async Task Execute_WithUsername_HashesAndBumpsSessionVersion()
    {
        var user = await TestDataSeeder.SeedUserAsync(_db, "alice", TestPasswords.Default);
        var originalVersion = user.SessionVersion;
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var code = await ResetPasswordCommand.ExecuteAsync(
            _db,
            _passwords,
            _logger,
            "alice",
            TestPasswords.Alternate,
            stdout,
            stderr);

        code.Should().Be(0);
        stderr.ToString().Should().BeEmpty();
        stdout.ToString().Should().Contain("alice");
        stdout.ToString().Should().Contain("Existing sessions are invalid");
        stdout.ToString().Should().NotContain(TestPasswords.Alternate);

        var reloaded = await _db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        reloaded.SessionVersion.Should().Be(originalVersion + 1);
        _passwords.VerifyPassword(TestPasswords.Alternate, reloaded.PasswordHash).Should().BeTrue();
        _passwords.VerifyPassword(TestPasswords.Default, reloaded.PasswordHash).Should().BeFalse();

        var log = string.Join('\n', _logger.Messages);
        log.Should().Contain("Password reset via break-glass");
        log.Should().Contain("alice");
        log.Should().NotContain(TestPasswords.Alternate);
        log.Should().NotContain(TestPasswords.Default);
    }

    [Fact]
    public async Task Execute_OmitUsername_ResetsSoleUser()
    {
        await TestDataSeeder.SeedUserAsync(_db, "alice", TestPasswords.Default);
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var code = await ResetPasswordCommand.ExecuteAsync(
            _db,
            _passwords,
            _logger,
            username: null,
            TestPasswords.Alternate,
            stdout,
            stderr);

        code.Should().Be(0);
        stdout.ToString().Should().Contain("alice");
        var reloaded = await _db.Users.AsNoTracking().SingleAsync();
        _passwords.VerifyPassword(TestPasswords.Alternate, reloaded.PasswordHash).Should().BeTrue();
        reloaded.SessionVersion.Should().Be(1);
    }

    [Fact]
    public async Task Execute_ZeroUsers_DoesNotInsert()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var code = await ResetPasswordCommand.ExecuteAsync(
            _db,
            _passwords,
            _logger,
            username: null,
            TestPasswords.Alternate,
            stdout,
            stderr);

        code.Should().Be(1);
        stderr.ToString().Should().Contain("Register");
        (await _db.Users.CountAsync()).Should().Be(0);
        _logger.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_TwoUsersWithoutUsername_DoesNotWrite()
    {
        var alice = await TestDataSeeder.SeedUserAsync(_db, "alice", TestPasswords.Default);
        var bob = await TestDataSeeder.SeedUserAsync(_db, "bob", TestPasswords.Default);
        var aliceHash = alice.PasswordHash;
        var bobHash = bob.PasswordHash;
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var code = await ResetPasswordCommand.ExecuteAsync(
            _db,
            _passwords,
            _logger,
            username: null,
            TestPasswords.Alternate,
            stdout,
            stderr);

        code.Should().Be(1);
        stderr.ToString().Should().Contain("--username");
        stderr.ToString().Should().Contain("alice");
        stderr.ToString().Should().Contain("bob");

        var reloadedAlice = await _db.Users.AsNoTracking().SingleAsync(u => u.Id == alice.Id);
        var reloadedBob = await _db.Users.AsNoTracking().SingleAsync(u => u.Id == bob.Id);
        reloadedAlice.PasswordHash.Should().Be(aliceHash);
        reloadedBob.PasswordHash.Should().Be(bobHash);
        reloadedAlice.SessionVersion.Should().Be(0);
        reloadedBob.SessionVersion.Should().Be(0);
    }

    [Fact]
    public async Task Execute_TwoUsersWithUsername_ResetsOnlyThatRow()
    {
        var alice = await TestDataSeeder.SeedUserAsync(_db, "alice", TestPasswords.Default);
        var bob = await TestDataSeeder.SeedUserAsync(_db, "bob", TestPasswords.Default);
        var bobHash = bob.PasswordHash;
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var code = await ResetPasswordCommand.ExecuteAsync(
            _db,
            _passwords,
            _logger,
            "alice",
            TestPasswords.Alternate,
            stdout,
            stderr);

        code.Should().Be(0);
        stdout.ToString().Should().Contain("alice");
        stdout.ToString().Should().NotContain("bob");

        var reloadedAlice = await _db.Users.AsNoTracking().SingleAsync(u => u.Id == alice.Id);
        var reloadedBob = await _db.Users.AsNoTracking().SingleAsync(u => u.Id == bob.Id);
        _passwords.VerifyPassword(TestPasswords.Alternate, reloadedAlice.PasswordHash).Should().BeTrue();
        reloadedAlice.SessionVersion.Should().Be(1);
        reloadedBob.PasswordHash.Should().Be(bobHash);
        reloadedBob.SessionVersion.Should().Be(0);
    }

    [Fact]
    public async Task TryReadPassword_ConfirmMismatch_DoesNotWrite()
    {
        var user = await TestDataSeeder.SeedUserAsync(_db, "alice", TestPasswords.Default);
        var originalHash = user.PasswordHash;
        ResetPasswordCommand.TryParse(["reset-password"], out var args, out _).Should().BeTrue();
        var input = new FakeResetPasswordInput { IsTty = true };
        input.Masked.Enqueue(TestPasswords.Alternate);
        input.Masked.Enqueue(TestPasswords.Default);
        var stderr = new StringWriter();

        ResetPasswordCommand.TryReadPassword(args!, input, stderr, out _, out var error).Should().BeFalse();
        error.Should().Contain("did not match");

        var reloaded = await _db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        reloaded.PasswordHash.Should().Be(originalHash);
        reloaded.SessionVersion.Should().Be(0);
    }

    [Fact]
    public async Task Execute_WeakPassword_DoesNotWrite()
    {
        var user = await TestDataSeeder.SeedUserAsync(_db, "alice", TestPasswords.Default);
        var originalHash = user.PasswordHash;
        var originalVersion = user.SessionVersion;
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var code = await ResetPasswordCommand.ExecuteAsync(
            _db,
            _passwords,
            _logger,
            "alice",
            "abcdefghijklmno",
            stdout,
            stderr);

        code.Should().Be(1);
        stderr.ToString().Should().Contain("16");
        stdout.ToString().Should().BeEmpty();

        var reloaded = await _db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        reloaded.PasswordHash.Should().Be(originalHash);
        reloaded.SessionVersion.Should().Be(originalVersion);
        _logger.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_UnknownUsername_DoesNotInsert()
    {
        await TestDataSeeder.SeedUserAsync(_db, "alice", TestPasswords.Default);
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var code = await ResetPasswordCommand.ExecuteAsync(
            _db,
            _passwords,
            _logger,
            "nobody",
            TestPasswords.Alternate,
            stdout,
            stderr);

        code.Should().Be(1);
        stderr.ToString().Should().Contain("not found");
        (await _db.Users.CountAsync()).Should().Be(1);
        (await _db.Users.AnyAsync(u => u.Username == "nobody")).Should().BeFalse();
    }

    [Fact]
    public async Task Execute_UsernameWrongCase_DoesNotMatch()
    {
        await TestDataSeeder.SeedUserAsync(_db, "alice", TestPasswords.Default);
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var code = await ResetPasswordCommand.ExecuteAsync(
            _db,
            _passwords,
            _logger,
            "Alice",
            TestPasswords.Alternate,
            stdout,
            stderr);

        code.Should().Be(1);
        stderr.ToString().Should().Contain("not found");
        var reloaded = await _db.Users.AsNoTracking().SingleAsync(u => u.Username == "alice");
        _passwords.VerifyPassword(TestPasswords.Default, reloaded.PasswordHash).Should().BeTrue();
        reloaded.SessionVersion.Should().Be(0);
    }

    [Fact]
    public async Task Execute_SameAsCurrent_RehashesAndBumpsSession()
    {
        var user = await TestDataSeeder.SeedUserAsync(_db, "alice", TestPasswords.Default);
        var originalHash = user.PasswordHash;
        var originalVersion = user.SessionVersion;
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var code = await ResetPasswordCommand.ExecuteAsync(
            _db,
            _passwords,
            _logger,
            "alice",
            TestPasswords.Default,
            stdout,
            stderr);

        code.Should().Be(0);
        stderr.ToString().Should().BeEmpty();

        var reloaded = await _db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        reloaded.PasswordHash.Should().NotBe(originalHash);
        reloaded.SessionVersion.Should().Be(originalVersion + 1);
        _passwords.VerifyPassword(TestPasswords.Default, reloaded.PasswordHash).Should().BeTrue();
    }

    private sealed class FakeResetPasswordInput : IResetPasswordInput
    {
        public bool IsTty { get; init; }
        public string? Line { get; init; }
        public Queue<string> Masked { get; } = new();

        public string? ReadLine() => Line;

        public string ReadMaskedLine() => Masked.Dequeue();
    }

    private sealed class ListLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose()
            {
            }
        }
    }
}
