using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.Services;

/// <summary>
/// AddApiKeyCiphertextAndSingleActiveKey must revoke all but each user's newest active key before adding the unique index.
/// </summary>
public class ApiKeySingleActiveMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260918010157_AddWorkoutSplitHeartRateBackfill";
    private const string TargetMigration = "20261025201000_AddApiKeyCiphertextAndSingleActiveKey";

    private string _cloneConnectionString = null!;
    private TempoDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _cloneConnectionString = await PostgresTestFixture.CreateCloneAsync();
        _db = PostgresTestFixture.CreateContext(_cloneConnectionString);
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
    public async Task Up_KeepsNewestActiveKeyPerUser_AndRevokesTheRest()
    {
        var migrator = _db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await InsertUserAsync(alice, "alice");
        await InsertUserAsync(bob, "bob");

        var aliceOld = await InsertKeyAsync(alice, t0, revokedAt: null);
        var aliceNewest = await InsertKeyAsync(alice, t0.AddDays(2), revokedAt: null);
        var aliceMiddle = await InsertKeyAsync(alice, t0.AddDays(1), revokedAt: null);
        var aliceAlreadyRevoked = await InsertKeyAsync(alice, t0.AddDays(3), revokedAt: t0.AddDays(4));
        var bobOnly = await InsertKeyAsync(bob, t0, revokedAt: null);

        await migrator.MigrateAsync(TargetMigration);

        var keys = await _db.ApiKeys.AsNoTracking().ToDictionaryAsync(k => k.Id);
        keys[aliceNewest].RevokedAt.Should().BeNull();
        keys[aliceOld].RevokedAt.Should().NotBeNull();
        keys[aliceMiddle].RevokedAt.Should().NotBeNull();
        keys[aliceAlreadyRevoked].RevokedAt.Should().Be(t0.AddDays(4));
        keys[bobOnly].RevokedAt.Should().BeNull();
        keys.Values.Should().OnlyContain(k => k.KeyCiphertext == null);
    }

    private async Task InsertUserAsync(Guid id, string username)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO "Users" ("Id", "Username", "PasswordHash", "CreatedAt") VALUES ({id}, {username}, 'h', now())""");
    }

    private async Task<Guid> InsertKeyAsync(Guid userId, DateTime createdAt, DateTime? revokedAt)
    {
        var id = Guid.NewGuid();
        var prefix = "tmp_" + id.ToString("N")[..12];
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "ApiKeys" ("Id", "UserId", "Label", "KeyHash", "KeyPrefix", "CreatedAt", "RevokedAt")
            VALUES ({id}, {userId}, NULL, 'sha256:x', {prefix}, {createdAt}, {revokedAt})
            """);
        return id;
    }
}
