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
    public DbSet<ApiKey> ApiKeys { get; set; }
    public DbSet<BestEffort> BestEfforts { get; set; }
    public DbSet<Shoe> Shoes { get; set; }
    public DbSet<ImportJob> ImportJobs { get; set; }
    public DbSet<WorkoutExternalIdentity> WorkoutExternalIdentities { get; set; }
    public DbSet<IntervalsIcuConnection> IntervalsIcuConnections { get; set; }
    public DbSet<CoachPreferences> CoachPreferences { get; set; }
    public DbSet<CoachProviderConnection> CoachProviderConnections { get; set; }
    public DbSet<CoachGoal> CoachGoals { get; set; }
    public DbSet<CoachMemoryLayer> CoachMemoryLayers { get; set; }
    public DbSet<CoachConstraint> CoachConstraints { get; set; }
    public DbSet<CoachCheckIn> CoachCheckIns { get; set; }
    public DbSet<CoachPrivacyPreflight> CoachPrivacyPreflights { get; set; }
    public DbSet<CoachJob> CoachJobs { get; set; }
    public DbSet<CoachWeek> CoachWeeks { get; set; }
    public DbSet<CoachAdjustment> CoachAdjustments { get; set; }
    public DbSet<CoachExchange> CoachExchanges { get; set; }
    public DbSet<CoachEpisode> CoachEpisodes { get; set; }
    public DbSet<CoachRule> CoachRules { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Workout>(entity =>
        {
            // Indexes for core stats (querying/filtering)
            entity.HasIndex(e => e.StartedAt);
            entity.HasIndex(e => new { e.StartedAt, e.DistanceM, e.DurationS }); // Duplicate detection
            entity.HasIndex(e => e.HealthKitUuid).IsUnique(); // HealthKit import idempotency
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
            entity.HasIndex(e => e.RawHealthKitData)
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

            entity.HasIndex(e => new { e.WorkoutId, e.Kind, e.Idx }).IsUnique();
            entity.ToTable(t => t.HasCheckConstraint(
                "CK_WorkoutSplits_Kind",
                "\"Kind\" IN ('distance', 'device_lap')"));
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

        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.HasOne(e => e.User)
                .WithMany(u => u.ApiKeys)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.UserId);
            // TryAuthenticateUserAsync: WHERE KeyPrefix = @p AND RevokedAt IS NULL
            entity.HasIndex(e => e.KeyPrefix)
                .HasFilter("\"RevokedAt\" IS NULL");
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

        modelBuilder.Entity<ImportJob>(entity =>
        {
            entity.HasIndex(e => e.Status);
            entity.Property(e => e.ErrorDetailsJson).HasColumnType("text");
            entity.Property(e => e.ResultJson).HasColumnType("text");
        });

        modelBuilder.Entity<WorkoutExternalIdentity>(entity =>
        {
            entity.HasOne(e => e.Workout)
                .WithMany(w => w.ExternalIdentities)
                .HasForeignKey(e => e.WorkoutId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.Source, e.ExternalId }).IsUnique();
            entity.HasIndex(e => new { e.WorkoutId, e.Source }).IsUnique();
        });

        modelBuilder.Entity<IntervalsIcuConnection>(entity =>
        {
            entity.Property(e => e.ApiKeyCiphertext).IsRequired();
            entity.Property(e => e.LastError).HasMaxLength(500);
        });

        modelBuilder.Entity<CoachPreferences>(entity =>
        {
            entity.HasIndex(e => e.Id).IsUnique();
        });

        modelBuilder.Entity<CoachProviderConnection>(entity =>
        {
            entity.Property(e => e.ApiKeyCiphertext).IsRequired();
            entity.Property(e => e.ModelId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.LastError).HasMaxLength(500);
        });

        modelBuilder.Entity<CoachGoal>(entity =>
        {
            entity.Property(e => e.Type).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Distance).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Notes).HasMaxLength(2000);
        });

        modelBuilder.Entity<CoachMemoryLayer>(entity =>
        {
            entity.Property(e => e.Layer).IsRequired().HasMaxLength(10);
            entity.HasIndex(e => e.Layer).IsUnique();
            entity.Property(e => e.Document).IsRequired().HasColumnType("jsonb");
        });

        modelBuilder.Entity<CoachConstraint>(entity =>
        {
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(40);
            entity.Property(e => e.Distance).HasMaxLength(50);
            entity.Property(e => e.Intent).HasMaxLength(20);
            entity.Property(e => e.Preset).HasMaxLength(40);
            entity.Property(e => e.Note).HasMaxLength(500);
            entity.Property(e => e.Weekdays).HasColumnType("jsonb");
            entity.HasIndex(e => e.Kind);
            entity.HasIndex(e => e.IsRetired);
        });

        modelBuilder.Entity<CoachCheckIn>(entity =>
        {
            entity.Property(e => e.OverallFeel).HasMaxLength(500);
            entity.Property(e => e.Niggles).HasMaxLength(2000);
            entity.Property(e => e.LifeConstraints).HasMaxLength(2000);
            entity.Property(e => e.RunNotesJson).IsRequired().HasColumnType("jsonb");
            entity.HasIndex(e => e.WeekStartDate).IsUnique();
        });

        modelBuilder.Entity<CoachPrivacyPreflight>(entity =>
        {
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(32);
            entity.Property(e => e.RedactedPayloadJson).IsRequired().HasColumnType("jsonb");
            entity.Property(e => e.PrivacyReportJson).IsRequired().HasColumnType("jsonb");
            entity.Property(e => e.PayloadHash).IsRequired().HasMaxLength(64);
            // One weekly preflight per plan Monday; many exchange prefights per week.
            entity.HasIndex(e => new { e.Kind, e.WeekStartDate })
                .IsUnique()
                .HasFilter("\"Kind\" = 'weekly'");
            entity.HasIndex(e => e.ExchangeId);
            entity.HasIndex(e => e.WeekStartDate);
        });

        modelBuilder.Entity<CoachExchange>(entity =>
        {
            entity.Property(e => e.AthleteText).IsRequired().HasMaxLength(4000);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(32);
            entity.Property(e => e.ReplyJson).HasColumnType("jsonb");
            entity.Property(e => e.SuggestedChangeJson).HasColumnType("jsonb");
            entity.Property(e => e.ErrorCode).HasMaxLength(32);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.HasIndex(e => e.WeekStartDate);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.WorkoutId);
            entity.HasIndex(e => e.JobId);
        });

        modelBuilder.Entity<CoachJob>(entity =>
        {
            entity.Property(e => e.Status).IsRequired().HasMaxLength(32);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(32);
            entity.Property(e => e.ErrorCode).HasMaxLength(32);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.WeekStartDate);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.AdjustmentId);
        });

        modelBuilder.Entity<CoachWeek>(entity =>
        {
            entity.Property(e => e.Status).IsRequired().HasMaxLength(32);
            entity.Property(e => e.CommitJson).IsRequired().HasColumnType("jsonb");
            entity.Property(e => e.PayloadHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.RedactedPayloadJson).IsRequired().HasColumnType("jsonb");
            entity.Property(e => e.ApplyRecordJson).HasColumnType("jsonb");
            entity.HasIndex(e => new { e.WeekStartDate, e.Status });
        });

        modelBuilder.Entity<CoachAdjustment>(entity =>
        {
            entity.Property(e => e.Reason).IsRequired().HasMaxLength(2000);
            entity.Property(e => e.Category).HasMaxLength(32);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(32);
            entity.HasIndex(e => e.WeekStartDate);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.SourceCommittedWeekId);
        });

        modelBuilder.Entity<CoachEpisode>(entity =>
        {
            entity.Property(e => e.Id).IsRequired().HasMaxLength(120);
            entity.Property(e => e.Category).IsRequired().HasMaxLength(40);
            entity.Property(e => e.Summary).IsRequired().HasMaxLength(400);
            entity.Property(e => e.Source).IsRequired().HasMaxLength(20);
            entity.HasIndex(e => e.Category);
            entity.HasIndex(e => e.Retired);
            entity.HasIndex(e => e.Date);
        });

        modelBuilder.Entity<CoachRule>(entity =>
        {
            entity.Property(e => e.Id).IsRequired().HasMaxLength(120);
            entity.Property(e => e.TargetLayer).IsRequired().HasMaxLength(60);
            entity.Property(e => e.RuleText).IsRequired();
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Evidence).IsRequired().HasColumnType("jsonb");
            entity.Property(e => e.Source).IsRequired().HasMaxLength(20);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.TargetLayer);
        });
    }
}

