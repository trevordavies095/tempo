using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkoutName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Workouts",
                type: "text",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Name",
                table: "Workouts");
        }
    }
}
