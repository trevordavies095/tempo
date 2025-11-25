using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Device",
                table: "Workouts",
                type: "text",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Device",
                table: "Workouts");
        }
    }
}
