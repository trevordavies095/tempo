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
    public void TryParse_MissingUsername_ReturnsError()
    {
        ResetPasswordCommand.TryParse(
                ["reset-password", "--password-stdin"],
                out var parsed,
                out var error)
            .Should().BeFalse();
        parsed.Should().BeNull();
        error.Should().Contain("--username");
    }

    [Fact]
    public void TryParse_MissingPasswordStdin_ReturnsError()
    {
        ResetPasswordCommand.TryParse(
                ["reset-password", "--username", "alice"],
                out var parsed,
                out var error)
            .Should().BeFalse();
        parsed.Should().BeNull();
        error.Should().Contain("--password-stdin");
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
