using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.Services;

/// <summary>#236: one-row UserSettings / IntervalsIcuConnections, enforced by Postgres.</summary>
public class SingletonRowsTests : IAsyncLifetime
{
    private const string MigrationBeforeSingletonIndex = "20261030224850_AddUserSettingsThresholds";

    private string _cs = null!;

    public async Task InitializeAsync() => _cs = await PostgresTestFixture.CreateCloneAsync();

    public Task DisposeAsync() => PostgresTestFixture.DropCloneAsync(_cs);

    [Fact]
    public async Task SingleRowAsync_IsOrdered_SoEfDoesNotWarn()
    {
        // Promote the warning to an exception: an unordered First would throw here
        await using var db = new TempoDbContext(new DbContextOptionsBuilder<TempoDbContext>()
            .UseNpgsql(_cs)
            .ConfigureWarnings(w => w.Throw(CoreEventId.FirstWithoutOrderByAndFilterWarning))
            .Options);

        (await db.UserSettings.SingleRowAsync()).Should().BeNull();
        (await db.UserSettings.AsNoTracking().Include(s => s.DefaultShoe).SingleRowAsync()).Should().BeNull();
        (await db.IntervalsIcuConnections.SingleRowAsync()).Should().BeNull();

        var unordered = async () => await db.UserSettings.FirstOrDefaultAsync();
        await unordered.Should().ThrowAsync<InvalidOperationException>("sanity: the guard is live");
    }

    [Fact]
    public async Task SingleRowAsync_ReturnsTheOneRow()
    {
        await using var db = PostgresTestFixture.CreateContext(_cs);
        var seeded = await TestDataSeeder.SeedUserSettingsAsync(db, age: 30);

        (await db.UserSettings.AsNoTracking().SingleRowAsync())!.Id.Should().Be(seeded.Id);
    }

    [Fact]
    public async Task SecondRow_IsRejected_AsSingletonConflict()
    {
        await using var db = PostgresTestFixture.CreateContext(_cs);
        db.UserSettings.Add(new UserSettings());
        await db.SaveChangesAsync();

        db.UserSettings.Add(new UserSettings());
        var act = async () => await db.SaveChangesAsync();

        (await act.Should().ThrowAsync<DbUpdateException>())
            .Which.Should().Match<DbUpdateException>(ex => SingletonRows.IsSingletonConflict(ex));
    }

    [Fact]
    public async Task SecondIntervalsIcuConnection_IsRejected_AsSingletonConflict()
    {
        await using var db = PostgresTestFixture.CreateContext(_cs);
        db.IntervalsIcuConnections.Add(new IntervalsIcuConnection { ApiKeyCiphertext = [1] });
        await db.SaveChangesAsync();

        db.IntervalsIcuConnections.Add(new IntervalsIcuConnection { ApiKeyCiphertext = [2] });
        var act = async () => await db.SaveChangesAsync();

        (await act.Should().ThrowAsync<DbUpdateException>())
            .Which.Should().Match<DbUpdateException>(ex => SingletonRows.IsSingletonConflict(ex));
    }

    [Fact]
    public async Task Retry_LosingTheInsertRace_UpdatesTheWinnersRow()
    {
        await using var loser = PostgresTestFixture.CreateContext(_cs);
        await using var winner = PostgresTestFixture.CreateContext(_cs);
        var attempts = 0;

        var result = await loser.RetryOnSingletonConflictAsync(async () =>
        {
            attempts++;
            var row = await loser.UserSettings.SingleRowAsync();
            if (attempts == 1)
            {
                // A concurrent request inserts between our read (null) and our save
                winner.UserSettings.Add(new UserSettings { UnitPreference = "metric", Age = 41 });
                await winner.SaveChangesAsync();
            }

            if (row == null)
            {
                row = new UserSettings();
                loser.UserSettings.Add(row);
            }

            row.UnitPreference = "imperial";
            await loser.SaveChangesAsync();
            return row;
        });

        attempts.Should().Be(2);
        await using var check = PostgresTestFixture.CreateContext(_cs);
        var rows = await check.UserSettings.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle();
        rows[0].UnitPreference.Should().Be("imperial");
        rows[0].Age.Should().Be(41, "the retry updated the winner's row instead of replacing it");
        result.Id.Should().Be(rows[0].Id);
    }

    [Fact]
    public async Task Migration_KeepsMostRecentlyUpdatedRow_ThenEnforcesOneRow()
    {
        await using var db = PostgresTestFixture.CreateContext(_cs);
        var migrator = db.GetService<IMigrator>();

        // Roll back to before the index, then reproduce the pre-#236 state: duplicate rows
        await migrator.MigrateAsync(MigrationBeforeSingletonIndex);
        var now = DateTime.UtcNow;
        db.UserSettings.AddRange(
            new UserSettings { UnitPreference = "metric", UpdatedAt = now.AddDays(-2) },
            new UserSettings { UnitPreference = "imperial", UpdatedAt = now },
            new UserSettings { UnitPreference = "metric", UpdatedAt = now.AddDays(-1) });
        db.IntervalsIcuConnections.AddRange(
            new IntervalsIcuConnection { ApiKeyCiphertext = [1], UpdatedAt = now.AddDays(-1) },
            new IntervalsIcuConnection { ApiKeyCiphertext = [2], UpdatedAt = now });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await migrator.MigrateAsync();

        var settings = await db.UserSettings.AsNoTracking().ToListAsync();
        settings.Should().ContainSingle().Which.UnitPreference.Should().Be("imperial");
        var connections = await db.IntervalsIcuConnections.AsNoTracking().ToListAsync();
        connections.Should().ContainSingle().Which.ApiKeyCiphertext.Should().Equal(2);

        db.UserSettings.Add(new UserSettings());
        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
