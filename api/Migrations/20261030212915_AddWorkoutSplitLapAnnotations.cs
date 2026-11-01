using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkoutSplitLapAnnotations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Intensity",
                table: "WorkoutSplits",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StepIndex",
                table: "WorkoutSplits",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Trigger",
                table: "WorkoutSplits",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Intensity",
                table: "WorkoutSplits");

            migrationBuilder.DropColumn(
                name: "StepIndex",
                table: "WorkoutSplits");

            migrationBuilder.DropColumn(
                name: "Trigger",
                table: "WorkoutSplits");
        }
    }
}
