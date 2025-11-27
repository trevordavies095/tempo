using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitPreferenceToUserSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UnitPreference",
                table: "UserSettings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Set default value "metric" for existing records
            migrationBuilder.Sql(@"
                UPDATE ""UserSettings""
                SET ""UnitPreference"" = 'metric'
                WHERE ""UnitPreference"" IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnitPreference",
                table: "UserSettings");
        }
    }
}
