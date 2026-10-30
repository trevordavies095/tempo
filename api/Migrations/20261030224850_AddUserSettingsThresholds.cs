using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSettingsThresholds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ThresholdHeartRateBpm",
                table: "UserSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ThresholdPaceS",
                table: "UserSettings",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThresholdHeartRateBpm",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "ThresholdPaceS",
                table: "UserSettings");
        }
    }
}
