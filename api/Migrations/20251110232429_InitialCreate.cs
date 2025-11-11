using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Workouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationS = table.Column<int>(type: "integer", nullable: false),
                    DistanceM = table.Column<double>(type: "double precision", nullable: false),
                    AvgPaceS = table.Column<int>(type: "integer", nullable: false),
                    ElevGainM = table.Column<double>(type: "double precision", nullable: true),
                    RunType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Weather = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workouts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkoutRoutes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    RouteGeoJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkoutRoutes_Workouts_WorkoutId",
                        column: x => x.WorkoutId,
                        principalTable: "Workouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkoutSplits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    Idx = table.Column<int>(type: "integer", nullable: false),
                    DistanceM = table.Column<double>(type: "double precision", nullable: false),
                    DurationS = table.Column<int>(type: "integer", nullable: false),
                    PaceS = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutSplits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkoutSplits_Workouts_WorkoutId",
                        column: x => x.WorkoutId,
                        principalTable: "Workouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutRoutes_WorkoutId",
                table: "WorkoutRoutes",
                column: "WorkoutId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Workouts_StartedAt",
                table: "Workouts",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Workouts_StartedAt_DistanceM_DurationS",
                table: "Workouts",
                columns: new[] { "StartedAt", "DistanceM", "DurationS" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSplits_WorkoutId_Idx",
                table: "WorkoutSplits",
                columns: new[] { "WorkoutId", "Idx" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkoutRoutes");

            migrationBuilder.DropTable(
                name: "WorkoutSplits");

            migrationBuilder.DropTable(
                name: "Workouts");
        }
    }
}
