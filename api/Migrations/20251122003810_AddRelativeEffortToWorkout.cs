using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRelativeEffortToWorkout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Use raw SQL to check if column exists before adding (idempotent migration)
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
            // Use raw SQL to check if column exists before dropping (idempotent migration)
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' 
                        AND table_name = 'Workouts' 
                        AND column_name = 'RelativeEffort'
                    ) THEN
                        ALTER TABLE ""Workouts""
                            DROP COLUMN ""RelativeEffort"";
                    END IF;
                END $$;
            ");
        }
    }
}
