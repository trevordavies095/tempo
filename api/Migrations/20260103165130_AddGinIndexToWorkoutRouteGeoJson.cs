using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGinIndexToWorkoutRouteGeoJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create GIN index on WorkoutRoutes.RouteGeoJson (idempotent)
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_indexes 
                        WHERE schemaname = 'public' 
                        AND tablename = 'WorkoutRoutes' 
                        AND indexname = 'IX_WorkoutRoutes_RouteGeoJson_GIN'
                    ) THEN
                        CREATE INDEX ""IX_WorkoutRoutes_RouteGeoJson_GIN"" 
                        ON ""WorkoutRoutes"" 
                        USING gin (""RouteGeoJson"");
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop GIN index (idempotent)
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM pg_indexes 
                        WHERE schemaname = 'public' 
                        AND tablename = 'WorkoutRoutes' 
                        AND indexname = 'IX_WorkoutRoutes_RouteGeoJson_GIN'
                    ) THEN
                        DROP INDEX ""IX_WorkoutRoutes_RouteGeoJson_GIN"";
                    END IF;
                END $$;
            ");
        }
    }
}

