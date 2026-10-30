using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkoutBestEfforts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BestEffortsComputedAt",
                table: "Workouts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkoutBestEfforts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    Distance = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DistanceM = table.Column<double>(type: "double precision", nullable: false),
                    TimeS = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutBestEfforts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkoutBestEfforts_Workouts_WorkoutId",
                        column: x => x.WorkoutId,
                        principalTable: "Workouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutBestEfforts_Distance_TimeS",
                table: "WorkoutBestEfforts",
                columns: new[] { "Distance", "TimeS" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutBestEfforts_WorkoutId_Distance",
                table: "WorkoutBestEfforts",
                columns: new[] { "WorkoutId", "Distance" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkoutBestEfforts");

            migrationBuilder.DropColumn(
                name: "BestEffortsComputedAt",
                table: "Workouts");
        }
    }
}
