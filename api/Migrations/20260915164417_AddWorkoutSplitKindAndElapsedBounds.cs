using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkoutSplitKindAndElapsedBounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "WorkoutSplits",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StartElapsedS",
                table: "WorkoutSplits",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EndElapsedS",
                table: "WorkoutSplits",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "StartDistanceM",
                table: "WorkoutSplits",
                type: "double precision",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "WorkoutSplits" SET "Kind" = 'distance' WHERE "Kind" IS NULL;
                """);

            // Pack Idx to 0..n-1 only for workouts that have duplicate (WorkoutId, Idx).
            migrationBuilder.Sql("""
                WITH dup_workouts AS (
                    SELECT "WorkoutId"
                    FROM "WorkoutSplits"
                    GROUP BY "WorkoutId", "Idx"
                    HAVING COUNT(*) > 1
                ),
                ranked AS (
                    SELECT s."Id",
                           (ROW_NUMBER() OVER (
                               PARTITION BY s."WorkoutId"
                               ORDER BY s."Idx", s."Id"
                           ) - 1) AS new_idx
                    FROM "WorkoutSplits" s
                    INNER JOIN dup_workouts d ON d."WorkoutId" = s."WorkoutId"
                )
                UPDATE "WorkoutSplits" AS s
                SET "Idx" = r.new_idx
                FROM ranked r
                WHERE s."Id" = r."Id";
                """);

            // Abutted elapsed bounds from cumulative DurationS (order by Idx, then Id).
            migrationBuilder.Sql("""
                WITH ordered AS (
                    SELECT "Id",
                           COALESCE(SUM("DurationS") OVER (
                               PARTITION BY "WorkoutId"
                               ORDER BY "Idx", "Id"
                               ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING
                           ), 0)::int AS start_elapsed,
                           SUM("DurationS") OVER (
                               PARTITION BY "WorkoutId"
                               ORDER BY "Idx", "Id"
                               ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                           )::int AS end_elapsed
                    FROM "WorkoutSplits"
                )
                UPDATE "WorkoutSplits" AS s
                SET "StartElapsedS" = o.start_elapsed,
                    "EndElapsedS" = o.end_elapsed
                FROM ordered o
                WHERE s."Id" = o."Id";
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Kind",
                table: "WorkoutSplits",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "distance",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "StartElapsedS",
                table: "WorkoutSplits",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "EndElapsedS",
                table: "WorkoutSplits",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.DropIndex(
                name: "IX_WorkoutSplits_WorkoutId_Idx",
                table: "WorkoutSplits");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSplits_WorkoutId_Kind_Idx",
                table: "WorkoutSplits",
                columns: new[] { "WorkoutId", "Kind", "Idx" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkoutSplits_Kind",
                table: "WorkoutSplits",
                sql: "\"Kind\" IN ('distance', 'device_lap')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkoutSplits_WorkoutId_Kind_Idx",
                table: "WorkoutSplits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkoutSplits_Kind",
                table: "WorkoutSplits");

            migrationBuilder.DropColumn(
                name: "EndElapsedS",
                table: "WorkoutSplits");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "WorkoutSplits");

            migrationBuilder.DropColumn(
                name: "StartDistanceM",
                table: "WorkoutSplits");

            migrationBuilder.DropColumn(
                name: "StartElapsedS",
                table: "WorkoutSplits");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSplits_WorkoutId_Idx",
                table: "WorkoutSplits",
                columns: new[] { "WorkoutId", "Idx" });
        }
    }
}
