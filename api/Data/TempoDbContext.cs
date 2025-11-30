using Microsoft.EntityFrameworkCore;
using Tempo.Api.Models;

namespace Tempo.Api.Data;

public class TempoDbContext : DbContext
{
    public TempoDbContext(DbContextOptions<TempoDbContext> options) : base(options)
    {
    }

    public DbSet<Workout> Workouts { get; set; }
    public DbSet<WorkoutRoute> WorkoutRoutes { get; set; }
    public DbSet<WorkoutSplit> WorkoutSplits { get; set; }
    public DbSet<WorkoutMedia> WorkoutMedia { get; set; }
    public DbSet<WorkoutTimeSeries> WorkoutTimeSeries { get; set; }
    public DbSet<UserSettings> UserSettings { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<BestEffort> BestEfforts { get; set; }
    public DbSet<Shoe> Shoes { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Workout>(entity =>
        {
            // Indexes for core stats (querying/filtering)
            entity.HasIndex(e => e.StartedAt);
            entity.HasIndex(e => new { e.StartedAt, e.DistanceM, e.DurationS }); // Duplicate detection
            entity.HasIndex(e => e.Source);
            entity.HasIndex(e => e.RunType);
            entity.HasIndex(e => e.ShoeId);
            
            // Foreign key relationship to Shoe
            entity.HasOne(e => e.Shoe)
                .WithMany(s => s.Workouts)
                .HasForeignKey(e => e.ShoeId)
                .OnDelete(DeleteBehavior.SetNull);
            
            // GIN indexes for JSONB fields (enables efficient JSON queries)
            entity.HasIndex(e => e.RawGpxData)
                .HasMethod("gin");
            entity.HasIndex(e => e.RawFitData)
                .HasMethod("gin");
            entity.HasIndex(e => e.RawStravaData)
                .HasMethod("gin");
            entity.HasIndex(e => e.Weather)
                .HasMethod("gin");
        });

        modelBuilder.Entity<WorkoutRoute>(entity =>
        {
            entity.HasOne(e => e.Workout)
                .WithOne(e => e.Route)
                .HasForeignKey<WorkoutRoute>(e => e.WorkoutId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkoutSplit>(entity =>
        {
            entity.HasOne(e => e.Workout)
                .WithMany(e => e.Splits)
                .HasForeignKey(e => e.WorkoutId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.WorkoutId, e.Idx });
        });

        modelBuilder.Entity<WorkoutMedia>(entity =>
        {
            entity.HasOne(e => e.Workout)
                .WithMany(e => e.Media)
                .HasForeignKey(e => e.WorkoutId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.WorkoutId);
        });

        modelBuilder.Entity<WorkoutTimeSeries>(entity =>
        {
            entity.HasOne(e => e.Workout)
                .WithMany(e => e.TimeSeries)
                .HasForeignKey(e => e.WorkoutId)
                .OnDelete(DeleteBehavior.Cascade);

            // Composite index for efficient time series queries
            entity.HasIndex(e => new { e.WorkoutId, e.ElapsedSeconds });
        });

        modelBuilder.Entity<UserSettings>(entity =>
        {
            // Single-row table pattern - ensure only one settings record exists
            // We'll enforce this in application logic, but add a unique constraint on Id
            entity.HasIndex(e => e.Id).IsUnique();
            entity.HasIndex(e => e.DefaultShoeId);
            
            // Foreign key relationship to Shoe
            entity.HasOne(e => e.DefaultShoe)
                .WithMany()
                .HasForeignKey(e => e.DefaultShoeId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(e => e.Username).IsUnique();
        });

        modelBuilder.Entity<BestEffort>(entity =>
        {
            entity.HasOne(e => e.Workout)
                .WithMany()
                .HasForeignKey(e => e.WorkoutId)
                .OnDelete(DeleteBehavior.Cascade);

            // Unique constraint: one best effort per distance
            entity.HasIndex(e => e.Distance).IsUnique();
            
            // Index for efficient queries
            entity.HasIndex(e => e.DistanceM);
        });
    }
}

