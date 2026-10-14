using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCoachGoalAndMemoryLayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CoachGoals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Distance = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DistanceM = table.Column<double>(type: "double precision", nullable: false),
                    RaceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TargetTimeS = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachGoals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CoachMemoryLayers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Layer = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Document = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachMemoryLayers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoachMemoryLayers_Layer",
                table: "CoachMemoryLayers",
                column: "Layer",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoachGoals");

            migrationBuilder.DropTable(
                name: "CoachMemoryLayers");
        }
    }
}
