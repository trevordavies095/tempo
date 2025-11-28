using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Use raw SQL to check if table exists before creating (idempotent migration)
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.tables 
                        WHERE table_schema = 'public' AND table_name = 'UserSettings'
                    ) THEN
                        -- Table doesn't exist - create it
                        CREATE TABLE ""UserSettings"" (
                            ""Id"" uuid NOT NULL,
                            ""Age"" integer,
                            ""MaxHeartRateBpm"" smallint,
                            ""RestingHeartRateBpm"" smallint,
                            ""ZoneCalculationMethod"" integer NOT NULL,
                            ""Zone1Min"" smallint,
                            ""Zone1Max"" smallint,
                            ""Zone2Min"" smallint,
                            ""Zone2Max"" smallint,
                            ""Zone3Min"" smallint,
                            ""Zone3Max"" smallint,
                            ""Zone4Min"" smallint,
                            ""Zone4Max"" smallint,
                            ""Zone5Min"" smallint,
                            ""Zone5Max"" smallint,
                            ""CreatedAt"" timestamp with time zone NOT NULL,
                            ""UpdatedAt"" timestamp with time zone NOT NULL,
                            CONSTRAINT ""PK_UserSettings"" PRIMARY KEY (""Id"")
                        );
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Use raw SQL to check if table exists before dropping (idempotent migration)
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.tables 
                        WHERE table_schema = 'public' AND table_name = 'UserSettings'
                    ) THEN
                        DROP TABLE ""UserSettings"";
                    END IF;
                END $$;
            ");
        }
    }
}
