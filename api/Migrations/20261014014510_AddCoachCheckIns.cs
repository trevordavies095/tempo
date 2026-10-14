using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCoachCheckIns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CoachCheckIns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WeekStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TimezoneOffsetMinutes = table.Column<int>(type: "integer", nullable: false),
                    OverallFeel = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Niggles = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LifeConstraints = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RunNotesJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachCheckIns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoachCheckIns_WeekStartDate",
                table: "CoachCheckIns",
                column: "WeekStartDate",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoachCheckIns");
        }
    }
}
