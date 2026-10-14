using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCoachConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CoachConstraints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Distance = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DistanceM = table.Column<double>(type: "double precision", nullable: true),
                    Intent = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Preset = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Weekdays = table.Column<string>(type: "jsonb", nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsRetired = table.Column<bool>(type: "boolean", nullable: false),
                    RetiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachConstraints", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoachConstraints_IsRetired",
                table: "CoachConstraints",
                column: "IsRetired");

            migrationBuilder.CreateIndex(
                name: "IX_CoachConstraints_Kind",
                table: "CoachConstraints",
                column: "Kind");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoachConstraints");
        }
    }
}
