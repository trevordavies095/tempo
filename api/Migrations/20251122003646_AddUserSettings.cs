using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Age = table.Column<int>(type: "integer", nullable: true),
                    MaxHeartRateBpm = table.Column<byte>(type: "smallint", nullable: true),
                    RestingHeartRateBpm = table.Column<byte>(type: "smallint", nullable: true),
                    ZoneCalculationMethod = table.Column<int>(type: "integer", nullable: false),
                    Zone1Min = table.Column<byte>(type: "smallint", nullable: true),
                    Zone1Max = table.Column<byte>(type: "smallint", nullable: true),
                    Zone2Min = table.Column<byte>(type: "smallint", nullable: true),
                    Zone2Max = table.Column<byte>(type: "smallint", nullable: true),
                    Zone3Min = table.Column<byte>(type: "smallint", nullable: true),
                    Zone3Max = table.Column<byte>(type: "smallint", nullable: true),
                    Zone4Min = table.Column<byte>(type: "smallint", nullable: true),
                    Zone4Max = table.Column<byte>(type: "smallint", nullable: true),
                    Zone5Min = table.Column<byte>(type: "smallint", nullable: true),
                    Zone5Max = table.Column<byte>(type: "smallint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserSettings");
        }
    }
}
