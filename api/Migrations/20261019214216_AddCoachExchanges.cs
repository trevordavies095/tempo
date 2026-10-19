using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCoachExchanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CoachPrivacyPreflights_WeekStartDate",
                table: "CoachPrivacyPreflights");

            migrationBuilder.AddColumn<Guid>(
                name: "ExchangeId",
                table: "CoachPrivacyPreflights",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "CoachPrivacyPreflights",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "weekly");

            migrationBuilder.CreateTable(
                name: "CoachExchanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WeekStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    WorkoutId = table.Column<Guid>(type: "uuid", nullable: true),
                    AthleteText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    PreflightId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReplyJson = table.Column<string>(type: "jsonb", nullable: true),
                    SuggestedChangeJson = table.Column<string>(type: "jsonb", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: true),
                    OutputTokens = table.Column<int>(type: "integer", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Locked = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachExchanges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoachPrivacyPreflights_ExchangeId",
                table: "CoachPrivacyPreflights",
                column: "ExchangeId");

            migrationBuilder.CreateIndex(
                name: "IX_CoachPrivacyPreflights_Kind_WeekStartDate",
                table: "CoachPrivacyPreflights",
                columns: new[] { "Kind", "WeekStartDate" },
                unique: true,
                filter: "\"Kind\" = 'weekly'");

            migrationBuilder.CreateIndex(
                name: "IX_CoachPrivacyPreflights_WeekStartDate",
                table: "CoachPrivacyPreflights",
                column: "WeekStartDate");

            migrationBuilder.CreateIndex(
                name: "IX_CoachExchanges_CreatedAt",
                table: "CoachExchanges",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CoachExchanges_JobId",
                table: "CoachExchanges",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_CoachExchanges_Status",
                table: "CoachExchanges",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CoachExchanges_WeekStartDate",
                table: "CoachExchanges",
                column: "WeekStartDate");

            migrationBuilder.CreateIndex(
                name: "IX_CoachExchanges_WorkoutId",
                table: "CoachExchanges",
                column: "WorkoutId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoachExchanges");

            migrationBuilder.DropIndex(
                name: "IX_CoachPrivacyPreflights_ExchangeId",
                table: "CoachPrivacyPreflights");

            migrationBuilder.DropIndex(
                name: "IX_CoachPrivacyPreflights_Kind_WeekStartDate",
                table: "CoachPrivacyPreflights");

            migrationBuilder.DropIndex(
                name: "IX_CoachPrivacyPreflights_WeekStartDate",
                table: "CoachPrivacyPreflights");

            migrationBuilder.DropColumn(
                name: "ExchangeId",
                table: "CoachPrivacyPreflights");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "CoachPrivacyPreflights");

            migrationBuilder.CreateIndex(
                name: "IX_CoachPrivacyPreflights_WeekStartDate",
                table: "CoachPrivacyPreflights",
                column: "WeekStartDate",
                unique: true);
        }
    }
}
