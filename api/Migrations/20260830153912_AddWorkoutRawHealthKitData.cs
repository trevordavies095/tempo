using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkoutRawHealthKitData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RawHealthKitData",
                table: "Workouts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Workouts_RawHealthKitData",
                table: "Workouts",
                column: "RawHealthKitData")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Workouts_RawHealthKitData",
                table: "Workouts");

            migrationBuilder.DropColumn(
                name: "RawHealthKitData",
                table: "Workouts");
        }
    }
}
