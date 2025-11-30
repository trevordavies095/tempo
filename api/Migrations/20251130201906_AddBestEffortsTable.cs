using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBestEffortsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BestEfforts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Distance = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DistanceM = table.Column<double>(type: "double precision", nullable: false),
                    TimeS = table.Column<int>(type: "integer", nullable: false),
                    WorkoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkoutDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CalculatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BestEfforts");
        }
    }
}
