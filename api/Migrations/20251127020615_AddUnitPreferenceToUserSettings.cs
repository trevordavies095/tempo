using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitPreferenceToUserSettings : Migration
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
                        AND table_name = 'UserSettings' 
                        AND column_name = 'UnitPreference'
                    ) THEN
                        -- Column doesn't exist - add it
                        ALTER TABLE ""UserSettings""
                            ADD COLUMN ""UnitPreference"" character varying(20);
                    END IF;
                END $$;
            ");

            // Set default value "metric" for existing records (safe to run multiple times)
            migrationBuilder.Sql(@"
                UPDATE ""UserSettings""
                SET ""UnitPreference"" = 'metric'
                WHERE ""UnitPreference"" IS NULL;
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
                        AND table_name = 'UserSettings' 
                        AND column_name = 'UnitPreference'
                    ) THEN
                        ALTER TABLE ""UserSettings""
                            DROP COLUMN ""UnitPreference"";
                    END IF;
                END $$;
            ");
        }
    }
}
