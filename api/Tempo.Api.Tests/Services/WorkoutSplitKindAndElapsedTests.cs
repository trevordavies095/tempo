using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class WorkoutSplitKindAndElapsedTests : IAsyncLifetime
{
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
    public void FillFromCumulativeDuration_AbutsWindows_AndSetsDistanceKind()
    {
        var splits = new List<WorkoutSplit>
        {
            new() { Id = Guid.NewGuid(), Idx = 1, DurationS = 200, Kind = "" },
            new() { Id = Guid.NewGuid(), Idx = 0, DurationS = 300, Kind = "  " },
            new() { Id = Guid.NewGuid(), Idx = 2, DurationS = 100 }
        };

        WorkoutSplitElapsed.FillFromCumulativeDuration(splits);

        var ordered = splits.OrderBy(s => s.Idx).ToList();
        ordered[0].Kind.Should().Be(WorkoutSplitKinds.Distance);
        ordered[0].StartElapsedS.Should().Be(0);
        ordered[0].EndElapsedS.Should().Be(300);
        ordered[1].StartElapsedS.Should().Be(300);
        ordered[1].EndElapsedS.Should().Be(500);
        ordered[2].StartElapsedS.Should().Be(500);
        ordered[2].EndElapsedS.Should().Be(600);
    }

    [Fact]
    public void NeedsCumulativeFill_TrueWhenAllEndsZeroAndAnyDurationPositive()
    {
        var splits = new List<WorkoutSplit>
        {
            new() { DurationS = 300, EndElapsedS = 0 },
            new() { DurationS = 200, EndElapsedS = 0 }
        };

        WorkoutSplitElapsed.NeedsCumulativeFill(splits).Should().BeTrue();
    }

    [Fact]
    public void NeedsCumulativeFill_FalseWhenBoundsAlreadyPresent()
    {
        var splits = new List<WorkoutSplit>
        {
            new() { DurationS = 300, StartElapsedS = 0, EndElapsedS = 300 },
            new() { DurationS = 200, StartElapsedS = 300, EndElapsedS = 500 }
        };

        WorkoutSplitElapsed.NeedsCumulativeFill(splits).Should().BeFalse();
    }

    [Fact]
    public async Task SaveChanges_RejectsDuplicateIdxWithinSameKind()
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db);
        var a = new WorkoutSplit
        {
            WorkoutId = workout.Id,
            Kind = WorkoutSplitKinds.Distance,
            Idx = 0,
            DistanceM = 1000,
            DurationS = 300,
            PaceS = 300,
            StartElapsedS = 0,
            EndElapsedS = 300
        };
        var b = new WorkoutSplit
        {
            WorkoutId = workout.Id,
            Kind = WorkoutSplitKinds.Distance,
            Idx = 0,
            DistanceM = 1000,
            DurationS = 310,
            PaceS = 310,
            StartElapsedS = 0,
            EndElapsedS = 310
        };
        _db.WorkoutSplits.AddRange(a, b);

        var act = async () => await _db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task SaveChanges_AllowsSameIdxAcrossKinds()
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db);
        _db.WorkoutSplits.AddRange(
            new WorkoutSplit
            {
                WorkoutId = workout.Id,
                Kind = WorkoutSplitKinds.Distance,
                Idx = 0,
                DistanceM = 1000,
                DurationS = 300,
                PaceS = 300,
                StartElapsedS = 0,
                EndElapsedS = 300
            },
            new WorkoutSplit
            {
                WorkoutId = workout.Id,
                Kind = WorkoutSplitKinds.DeviceLap,
                Idx = 0,
                DistanceM = 1000,
                DurationS = 280,
                PaceS = 280,
                StartElapsedS = 0,
                EndElapsedS = 290
            });

        await _db.SaveChangesAsync();

        (await _db.WorkoutSplits.CountAsync(s => s.WorkoutId == workout.Id)).Should().Be(2);
    }

    [Fact]
    public void Deserialize_OldSplitJson_MissingKindAndElapsed_FillsLikeMigrate()
    {
        var workoutId = Guid.NewGuid();
        var splitId0 = Guid.NewGuid();
        var splitId1 = Guid.NewGuid();
        var json = $$"""
            [
              { "id": "{{splitId0}}", "workoutId": "{{workoutId}}", "idx": 0, "distanceM": 1000, "durationS": 300, "paceS": 300 },
              { "id": "{{splitId1}}", "workoutId": "{{workoutId}}", "idx": 1, "distanceM": 1000, "durationS": 320, "paceS": 320 }
            ]
            """;

        var splits = System.Text.Json.JsonSerializer.Deserialize<List<WorkoutSplit>>(json, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        })!;

        foreach (var split in splits)
        {
            if (string.IsNullOrWhiteSpace(split.Kind))
            {
                split.Kind = WorkoutSplitKinds.Distance;
            }
        }

        WorkoutSplitElapsed.NeedsCumulativeFill(splits).Should().BeTrue();
        WorkoutSplitElapsed.FillFromCumulativeDuration(splits);

        splits[0].Kind.Should().Be(WorkoutSplitKinds.Distance);
        splits[0].StartElapsedS.Should().Be(0);
        splits[0].EndElapsedS.Should().Be(300);
        splits[1].StartElapsedS.Should().Be(300);
        splits[1].EndElapsedS.Should().Be(620);
        splits.Should().OnlyContain(s => s.StartDistanceM == null);
    }
}
