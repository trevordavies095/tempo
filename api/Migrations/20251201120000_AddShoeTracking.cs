using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddShoeTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create Shoes table (idempotent)
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.tables 
                        WHERE table_schema = 'public' 
                        AND table_name = 'Shoes'
                    ) THEN
                        CREATE TABLE ""Shoes"" (
                            ""Id"" uuid NOT NULL,
                            ""Brand"" character varying(100) NOT NULL,
                            ""Model"" character varying(100) NOT NULL,
                            ""InitialMileageM"" double precision,
                            ""CreatedAt"" timestamp with time zone NOT NULL,
                            ""UpdatedAt"" timestamp with time zone NOT NULL,
                            CONSTRAINT ""PK_Shoes"" PRIMARY KEY (""Id"")
                        );
                    END IF;
                END $$;
            ");

            // Add ShoeId column to Workouts table (idempotent)
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' 
                        AND table_name = 'Workouts' 
                        AND column_name = 'ShoeId'
                    ) THEN
                        ALTER TABLE ""Workouts""
                            ADD COLUMN ""ShoeId"" uuid;
                    END IF;
                END $$;
            ");

            // Add DefaultShoeId column to UserSettings table (idempotent)
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' 
                        AND table_name = 'UserSettings' 
                        AND column_name = 'DefaultShoeId'
                    ) THEN
                        ALTER TABLE ""UserSettings""
                            ADD COLUMN ""DefaultShoeId"" uuid;
                    END IF;
                END $$;
            ");

            // Create foreign key constraint for Workouts.ShoeId (idempotent)
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.table_constraints 
                        WHERE constraint_schema = 'public' 
                        AND constraint_name = 'FK_Workouts_Shoes_ShoeId'
                    ) THEN
                        ALTER TABLE ""Workouts""
                            ADD CONSTRAINT ""FK_Workouts_Shoes_ShoeId""
                            FOREIGN KEY (""ShoeId"")
                            REFERENCES ""Shoes"" (""Id"")
                            ON DELETE SET NULL;
                    END IF;
                END $$;
            ");

            // Create foreign key constraint for UserSettings.DefaultShoeId (idempotent)
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.table_constraints 
                        WHERE constraint_schema = 'public' 
                        AND constraint_name = 'FK_UserSettings_Shoes_DefaultShoeId'
                    ) THEN
                        ALTER TABLE ""UserSettings""
                            ADD CONSTRAINT ""FK_UserSettings_Shoes_DefaultShoeId""
                            FOREIGN KEY (""DefaultShoeId"")
                            REFERENCES ""Shoes"" (""Id"")
                            ON DELETE SET NULL;
                    END IF;
                END $$;
            ");

            // Create index on Workouts.ShoeId (idempotent)
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_indexes 
                        WHERE schemaname = 'public' 
                        AND tablename = 'Workouts' 
                        AND indexname = 'IX_Workouts_ShoeId'
                    ) THEN
                        CREATE INDEX ""IX_Workouts_ShoeId"" ON ""Workouts"" (""ShoeId"");
                    END IF;
                END $$;
            ");

            // Create index on UserSettings.DefaultShoeId (idempotent)
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_indexes 
                        WHERE schemaname = 'public' 
                        AND tablename = 'UserSettings' 
                        AND indexname = 'IX_UserSettings_DefaultShoeId'
                    ) THEN
                        CREATE INDEX ""IX_UserSettings_DefaultShoeId"" ON ""UserSettings"" (""DefaultShoeId"");
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop indexes
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM pg_indexes 
                        WHERE schemaname = 'public' 
                        AND indexname = 'IX_UserSettings_DefaultShoeId'
                    ) THEN
                        DROP INDEX ""IX_UserSettings_DefaultShoeId"";
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM pg_indexes 
                        WHERE schemaname = 'public' 
                        AND indexname = 'IX_Workouts_ShoeId'
                    ) THEN
                        DROP INDEX ""IX_Workouts_ShoeId"";
                    END IF;
                END $$;
            ");

            // Drop foreign key constraints
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.table_constraints 
                        WHERE constraint_schema = 'public' 
                        AND constraint_name = 'FK_UserSettings_Shoes_DefaultShoeId'
                    ) THEN
                        ALTER TABLE ""UserSettings""
                            DROP CONSTRAINT ""FK_UserSettings_Shoes_DefaultShoeId"";
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.table_constraints 
                        WHERE constraint_schema = 'public' 
                        AND constraint_name = 'FK_Workouts_Shoes_ShoeId'
                    ) THEN
                        ALTER TABLE ""Workouts""
                            DROP CONSTRAINT ""FK_Workouts_Shoes_ShoeId"";
                    END IF;
                END $$;
            ");

            // Drop columns
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' 
                        AND table_name = 'UserSettings' 
                        AND column_name = 'DefaultShoeId'
                    ) THEN
                        ALTER TABLE ""UserSettings""
                            DROP COLUMN ""DefaultShoeId"";
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_schema = 'public' 
                        AND table_name = 'Workouts' 
                        AND column_name = 'ShoeId'
                    ) THEN
                        ALTER TABLE ""Workouts""
                            DROP COLUMN ""ShoeId"";
                    END IF;
                END $$;
            ");

            // Drop Shoes table
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.tables 
                        WHERE table_schema = 'public' 
                        AND table_name = 'Shoes'
                    ) THEN
                        DROP TABLE ""Shoes"";
                    END IF;
                END $$;
            ");
        }
    }
}

