using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCoachPrivacyPreflights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CoachPrivacyPreflights",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WeekStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TimezoneOffsetMinutes = table.Column<int>(type: "integer", nullable: false),
                    ReferenceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RedactedPayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    PrivacyReportJson = table.Column<string>(type: "jsonb", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssembledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsentedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachPrivacyPreflights", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoachPrivacyPreflights_WeekStartDate",
                table: "CoachPrivacyPreflights",
                column: "WeekStartDate",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoachPrivacyPreflights");
        }
    }
}
