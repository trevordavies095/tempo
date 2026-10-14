using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCoachAdjustmentsAndJobKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AdjustmentId",
                table: "CoachJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "CoachJobs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "generate");

            migrationBuilder.CreateTable(
                name: "CoachAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WeekStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceCommittedWeekId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    LockedThroughDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachAdjustments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoachJobs_AdjustmentId",
                table: "CoachJobs",
                column: "AdjustmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CoachAdjustments_SourceCommittedWeekId",
                table: "CoachAdjustments",
                column: "SourceCommittedWeekId");

            migrationBuilder.CreateIndex(
                name: "IX_CoachAdjustments_Status",
                table: "CoachAdjustments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CoachAdjustments_WeekStartDate",
                table: "CoachAdjustments",
                column: "WeekStartDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoachAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_CoachJobs_AdjustmentId",
                table: "CoachJobs");

            migrationBuilder.DropColumn(
                name: "AdjustmentId",
                table: "CoachJobs");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "CoachJobs");
        }
    }
}
