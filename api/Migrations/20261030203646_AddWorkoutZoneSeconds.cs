using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkoutZoneSeconds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ZoneSecondsComputedAt",
                table: "Workouts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ZoneSecondsZ1",
                table: "Workouts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ZoneSecondsZ2",
                table: "Workouts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ZoneSecondsZ3",
                table: "Workouts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ZoneSecondsZ4",
                table: "Workouts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ZoneSecondsZ5",
                table: "Workouts",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ZoneSecondsComputedAt",
                table: "Workouts");

            migrationBuilder.DropColumn(
                name: "ZoneSecondsZ1",
                table: "Workouts");

            migrationBuilder.DropColumn(
                name: "ZoneSecondsZ2",
                table: "Workouts");

            migrationBuilder.DropColumn(
                name: "ZoneSecondsZ3",
                table: "Workouts");

            migrationBuilder.DropColumn(
                name: "ZoneSecondsZ4",
                table: "Workouts");

            migrationBuilder.DropColumn(
                name: "ZoneSecondsZ5",
                table: "Workouts");
        }
    }
}
