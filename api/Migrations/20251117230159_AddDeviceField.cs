using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceField : Migration
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
                        AND column_name = 'Device'
                    ) THEN
                        -- Column doesn't exist - add it
                        ALTER TABLE ""Workouts""
                            ADD COLUMN ""Device"" text;
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
                        AND column_name = 'Device'
                    ) THEN
                        ALTER TABLE ""Workouts""
                            DROP COLUMN ""Device"";
                    END IF;
                END $$;
            ");
        }
    }
}
