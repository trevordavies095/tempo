using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class DropBestEffortsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Seed per-workout rows from the old records so all-time bests stay correct until the
            // backfill reaches those workouts (their marker stays null, so the backfill replaces these rows).
            migrationBuilder.Sql("""
                INSERT INTO "WorkoutBestEfforts" ("Id", "WorkoutId", "Distance", "DistanceM", "TimeS")
                SELECT gen_random_uuid(), be."WorkoutId", be."Distance", be."DistanceM", be."TimeS"
                FROM "BestEfforts" be
                JOIN "Workouts" w ON w."Id" = be."WorkoutId"
                WHERE w."BestEffortsComputedAt" IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM "WorkoutBestEfforts" wbe
                      WHERE wbe."WorkoutId" = be."WorkoutId" AND wbe."Distance" = be."Distance");
                """);

            migrationBuilder.DropTable(
                name: "BestEfforts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BestEfforts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalculatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Distance = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DistanceM = table.Column<double>(type: "double precision", nullable: false),
                    TimeS = table.Column<int>(type: "integer", nullable: false),
                    WorkoutDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BestEfforts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BestEfforts_Workouts_WorkoutId",
                        column: x => x.WorkoutId,
                        principalTable: "Workouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BestEfforts_Distance",
                table: "BestEfforts",
                column: "Distance",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BestEfforts_DistanceM",
                table: "BestEfforts",
                column: "DistanceM");

            migrationBuilder.CreateIndex(
                name: "IX_BestEfforts_WorkoutId",
                table: "BestEfforts",
                column: "WorkoutId");
        }
    }
}
