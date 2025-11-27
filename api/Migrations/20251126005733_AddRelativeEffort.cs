using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRelativeEffort : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Check if RelativeEffort column already exists (from AddRelativeEffortToWorkout migration)
            // Use raw SQL to conditionally add the column
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' 
                        AND table_name = 'Workouts' 
                        AND column_name = 'RelativeEffort'
                    ) THEN
                        -- Column doesn't exist - add it
                        ALTER TABLE ""Workouts""
                            ADD COLUMN ""RelativeEffort"" integer;
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Check if RelativeEffort column exists before dropping it
            // Only drop if this migration added it (i.e., if AddRelativeEffortToWorkout wasn't applied)
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' 
                        AND table_name = 'Workouts' 
                        AND column_name = 'RelativeEffort'
                    ) THEN
                        -- Check if this column was added by AddRelativeEffortToWorkout migration
                        -- If that migration was applied, we shouldn't drop it here
                        -- We'll only drop if we can't determine the source
                        -- For safety, we'll check if the migration history shows AddRelativeEffortToWorkout
                        IF NOT EXISTS (
                            SELECT 1 FROM ""__EFMigrationsHistory""
                            WHERE ""MigrationId"" = '20251122003810_AddRelativeEffortToWorkout'
                        ) THEN
                            -- AddRelativeEffortToWorkout wasn't applied, so this migration added it
                            -- Safe to drop
                            ALTER TABLE ""Workouts""
                                DROP COLUMN IF EXISTS ""RelativeEffort"";
                        END IF;
                    END IF;
                END $$;
            ");
        }
    }
}
