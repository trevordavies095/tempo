using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCoachEpisodesAndRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PendingMemoryUpgradeNotice",
                table: "CoachPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CoachEpisodes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Summary = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    SourceWeekEnding = table.Column<DateOnly>(type: "date", nullable: true),
                    SourceWeekId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Retired = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachEpisodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CoachRules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TargetLayer = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    RuleText = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Evidence = table.Column<string>(type: "jsonb", nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachRules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoachEpisodes_Category",
                table: "CoachEpisodes",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_CoachEpisodes_Date",
                table: "CoachEpisodes",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_CoachEpisodes_Retired",
                table: "CoachEpisodes",
                column: "Retired");

            migrationBuilder.CreateIndex(
                name: "IX_CoachRules_Status",
                table: "CoachRules",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CoachRules_TargetLayer",
                table: "CoachRules",
                column: "TargetLayer");

            // Drop slice-1 L2/L3 acceptedProposals blobs (never consumed by assemble).
            migrationBuilder.Sql(
                """
                DELETE FROM "CoachMemoryLayers" WHERE "Layer" IN ('L2', 'L3');
                """);

            // Discard open drafts that cannot survive the typed-memory contract change.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                  draft_count integer;
                BEGIN
                  SELECT COUNT(*) INTO draft_count FROM "CoachWeeks" WHERE "Status" = 'draft';

                  DELETE FROM "CoachJobs"
                  WHERE "Status" NOT IN ('ready', 'failed', 'cancelled', 'completed')
                     OR "WeekStartDate" IN (
                          SELECT "WeekStartDate" FROM "CoachWeeks" WHERE "Status" = 'draft'
                     );

                  DELETE FROM "CoachAdjustments" WHERE "Status" = 'started';

                  DELETE FROM "CoachWeeks" WHERE "Status" = 'draft';

                  IF draft_count > 0 THEN
                    UPDATE "CoachPreferences"
                    SET "PendingMemoryUpgradeNotice" = TRUE,
                        "UpdatedAt" = NOW() AT TIME ZONE 'utc';
                  END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoachEpisodes");

            migrationBuilder.DropTable(
                name: "CoachRules");

            migrationBuilder.DropColumn(
                name: "PendingMemoryUpgradeNotice",
                table: "CoachPreferences");
        }
    }
}
