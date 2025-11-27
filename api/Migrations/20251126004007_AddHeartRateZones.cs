using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHeartRateZones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Use raw SQL to handle both cases: table exists (alter) or doesn't exist (create)
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.tables 
                        WHERE table_schema = 'public' AND table_name = 'UserSettings'
                    ) THEN
                        -- Table exists - alter it to new schema
                        -- Drop old columns
                        ALTER TABLE ""UserSettings""
                            DROP COLUMN IF EXISTS ""ZoneCalculationMethod"",
                            DROP COLUMN IF EXISTS ""Zone1Min"",
                            DROP COLUMN IF EXISTS ""Zone1Max"",
                            DROP COLUMN IF EXISTS ""Zone2Min"",
                            DROP COLUMN IF EXISTS ""Zone2Max"",
                            DROP COLUMN IF EXISTS ""Zone3Min"",
                            DROP COLUMN IF EXISTS ""Zone3Max"",
                            DROP COLUMN IF EXISTS ""Zone4Min"",
                            DROP COLUMN IF EXISTS ""Zone4Max"",
                            DROP COLUMN IF EXISTS ""Zone5Min"",
                            DROP COLUMN IF EXISTS ""Zone5Max"";

                        -- Alter existing columns from byte (smallint) to int
                        ALTER TABLE ""UserSettings""
                            ALTER COLUMN ""MaxHeartRateBpm"" TYPE integer USING ""MaxHeartRateBpm""::integer,
                            ALTER COLUMN ""RestingHeartRateBpm"" TYPE integer USING ""RestingHeartRateBpm""::integer;

                        -- Add new columns (only if they don't exist)
                        ALTER TABLE ""UserSettings""
                            ADD COLUMN IF NOT EXISTS ""CalculationMethod"" integer NOT NULL DEFAULT 0,
                            ADD COLUMN IF NOT EXISTS ""Zone1MinBpm"" integer NOT NULL DEFAULT 0,
                            ADD COLUMN IF NOT EXISTS ""Zone1MaxBpm"" integer NOT NULL DEFAULT 0,
                            ADD COLUMN IF NOT EXISTS ""Zone2MinBpm"" integer NOT NULL DEFAULT 0,
                            ADD COLUMN IF NOT EXISTS ""Zone2MaxBpm"" integer NOT NULL DEFAULT 0,
                            ADD COLUMN IF NOT EXISTS ""Zone3MinBpm"" integer NOT NULL DEFAULT 0,
                            ADD COLUMN IF NOT EXISTS ""Zone3MaxBpm"" integer NOT NULL DEFAULT 0,
                            ADD COLUMN IF NOT EXISTS ""Zone4MinBpm"" integer NOT NULL DEFAULT 0,
                            ADD COLUMN IF NOT EXISTS ""Zone4MaxBpm"" integer NOT NULL DEFAULT 0,
                            ADD COLUMN IF NOT EXISTS ""Zone5MinBpm"" integer NOT NULL DEFAULT 0,
                            ADD COLUMN IF NOT EXISTS ""Zone5MaxBpm"" integer NOT NULL DEFAULT 0;

                        -- Create index if it doesn't exist
                        CREATE INDEX IF NOT EXISTS ""IX_UserSettings_Id"" ON ""UserSettings"" (""Id"");
                    ELSE
                        -- Table doesn't exist - create it with new schema
                        CREATE TABLE ""UserSettings"" (
                            ""Id"" uuid NOT NULL,
                            ""CalculationMethod"" integer NOT NULL,
                            ""Age"" integer,
                            ""RestingHeartRateBpm"" integer,
                            ""MaxHeartRateBpm"" integer,
                            ""Zone1MinBpm"" integer NOT NULL,
                            ""Zone1MaxBpm"" integer NOT NULL,
                            ""Zone2MinBpm"" integer NOT NULL,
                            ""Zone2MaxBpm"" integer NOT NULL,
                            ""Zone3MinBpm"" integer NOT NULL,
                            ""Zone3MaxBpm"" integer NOT NULL,
                            ""Zone4MinBpm"" integer NOT NULL,
                            ""Zone4MaxBpm"" integer NOT NULL,
                            ""Zone5MinBpm"" integer NOT NULL,
                            ""Zone5MaxBpm"" integer NOT NULL,
                            ""CreatedAt"" timestamp with time zone NOT NULL,
                            ""UpdatedAt"" timestamp with time zone NOT NULL,
                            CONSTRAINT ""PK_UserSettings"" PRIMARY KEY (""Id"")
                        );

                        CREATE UNIQUE INDEX ""IX_UserSettings_Id"" ON ""UserSettings"" (""Id"");
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Use raw SQL to handle the down migration
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.tables 
                        WHERE table_schema = 'public' AND table_name = 'UserSettings'
                    ) THEN
                        -- Check if table has new schema (CalculationMethod column exists)
                        IF EXISTS (
                            SELECT 1 FROM information_schema.columns 
                            WHERE table_schema = 'public' 
                            AND table_name = 'UserSettings' 
                            AND column_name = 'CalculationMethod'
                        ) THEN
                            -- Revert to old schema
                            -- Drop new columns
                            DROP INDEX IF EXISTS ""IX_UserSettings_Id"";
                            
                            ALTER TABLE ""UserSettings""
                                DROP COLUMN IF EXISTS ""CalculationMethod"",
                                DROP COLUMN IF EXISTS ""Zone1MinBpm"",
                                DROP COLUMN IF EXISTS ""Zone1MaxBpm"",
                                DROP COLUMN IF EXISTS ""Zone2MinBpm"",
                                DROP COLUMN IF EXISTS ""Zone2MaxBpm"",
                                DROP COLUMN IF EXISTS ""Zone3MinBpm"",
                                DROP COLUMN IF EXISTS ""Zone3MaxBpm"",
                                DROP COLUMN IF EXISTS ""Zone4MinBpm"",
                                DROP COLUMN IF EXISTS ""Zone4MaxBpm"",
                                DROP COLUMN IF EXISTS ""Zone5MinBpm"",
                                DROP COLUMN IF EXISTS ""Zone5MaxBpm"";

                            -- Revert column types from int to byte (smallint)
                            ALTER TABLE ""UserSettings""
                                ALTER COLUMN ""MaxHeartRateBpm"" TYPE smallint USING ""MaxHeartRateBpm""::smallint,
                                ALTER COLUMN ""RestingHeartRateBpm"" TYPE smallint USING ""RestingHeartRateBpm""::smallint;

                            -- Add back old columns
                            ALTER TABLE ""UserSettings""
                                ADD COLUMN IF NOT EXISTS ""ZoneCalculationMethod"" integer NOT NULL DEFAULT 0,
                                ADD COLUMN IF NOT EXISTS ""Zone1Min"" smallint,
                                ADD COLUMN IF NOT EXISTS ""Zone1Max"" smallint,
                                ADD COLUMN IF NOT EXISTS ""Zone2Min"" smallint,
                                ADD COLUMN IF NOT EXISTS ""Zone2Max"" smallint,
                                ADD COLUMN IF NOT EXISTS ""Zone3Min"" smallint,
                                ADD COLUMN IF NOT EXISTS ""Zone3Max"" smallint,
                                ADD COLUMN IF NOT EXISTS ""Zone4Min"" smallint,
                                ADD COLUMN IF NOT EXISTS ""Zone4Max"" smallint,
                                ADD COLUMN IF NOT EXISTS ""Zone5Min"" smallint,
                                ADD COLUMN IF NOT EXISTS ""Zone5Max"" smallint;
                        ELSE
                            -- Table was created by this migration, drop it
                            DROP TABLE ""UserSettings"";
                        END IF;
                    END IF;
                END $$;
            ");
        }
    }
}
