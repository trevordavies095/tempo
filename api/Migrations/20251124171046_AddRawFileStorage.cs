using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRawFileStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Use raw SQL to check if columns exist before adding (idempotent migration)
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    -- Add RawFileData column if it doesn't exist
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' 
                        AND table_name = 'Workouts' 
                        AND column_name = 'RawFileData'
                    ) THEN
                        ALTER TABLE ""Workouts""
                            ADD COLUMN ""RawFileData"" bytea;
                    END IF;

                    -- Add RawFileName column if it doesn't exist
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' 
                        AND table_name = 'Workouts' 
                        AND column_name = 'RawFileName'
                    ) THEN
                        ALTER TABLE ""Workouts""
                            ADD COLUMN ""RawFileName"" character varying(255);
                    END IF;

                    -- Add RawFileType column if it doesn't exist
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' 
                        AND table_name = 'Workouts' 
                        AND column_name = 'RawFileType'
                    ) THEN
                        ALTER TABLE ""Workouts""
                            ADD COLUMN ""RawFileType"" character varying(10);
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Use raw SQL to check if columns exist before dropping (idempotent migration)
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' 
                        AND table_name = 'Workouts' 
                        AND column_name = 'RawFileData'
                    ) THEN
                        ALTER TABLE ""Workouts""
                            DROP COLUMN ""RawFileData"";
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' 
                        AND table_name = 'Workouts' 
                        AND column_name = 'RawFileName'
                    ) THEN
                        ALTER TABLE ""Workouts""
                            DROP COLUMN ""RawFileName"";
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' 
                        AND table_name = 'Workouts' 
                        AND column_name = 'RawFileType'
                    ) THEN
                        ALTER TABLE ""Workouts""
                            DROP COLUMN ""RawFileType"";
                    END IF;
                END $$;
            ");
        }
    }
}
