using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCoachJobsAndWeeks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CoachJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    WeekStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PreflightId = table.Column<Guid>(type: "uuid", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: true),
                    OutputTokens = table.Column<int>(type: "integer", nullable: true),
                    SchemaRepairAttempted = table.Column<bool>(type: "boolean", nullable: false),
                    ResultWeekId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CoachWeeks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WeekStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CommitJson = table.Column<string>(type: "jsonb", nullable: false),
                    PreflightId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RedactedPayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachWeeks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoachJobs_CreatedAt",
                table: "CoachJobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CoachJobs_Status",
                table: "CoachJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CoachJobs_WeekStartDate",
                table: "CoachJobs",
                column: "WeekStartDate");

            migrationBuilder.CreateIndex(
                name: "IX_CoachWeeks_WeekStartDate_Status",
                table: "CoachWeeks",
                columns: new[] { "WeekStartDate", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoachJobs");

            migrationBuilder.DropTable(
                name: "CoachWeeks");
        }
    }
}
