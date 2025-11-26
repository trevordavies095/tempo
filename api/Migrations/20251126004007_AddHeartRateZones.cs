using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHeartRateZones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalculationMethod = table.Column<int>(type: "integer", nullable: false),
                    Age = table.Column<int>(type: "integer", nullable: true),
                    RestingHeartRateBpm = table.Column<int>(type: "integer", nullable: true),
                    MaxHeartRateBpm = table.Column<int>(type: "integer", nullable: true),
                    Zone1MinBpm = table.Column<int>(type: "integer", nullable: false),
                    Zone1MaxBpm = table.Column<int>(type: "integer", nullable: false),
                    Zone2MinBpm = table.Column<int>(type: "integer", nullable: false),
                    Zone2MaxBpm = table.Column<int>(type: "integer", nullable: false),
                    Zone3MinBpm = table.Column<int>(type: "integer", nullable: false),
                    Zone3MaxBpm = table.Column<int>(type: "integer", nullable: false),
                    Zone4MinBpm = table.Column<int>(type: "integer", nullable: false),
                    Zone4MaxBpm = table.Column<int>(type: "integer", nullable: false),
                    Zone5MinBpm = table.Column<int>(type: "integer", nullable: false),
                    Zone5MaxBpm = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserSettings_Id",
                table: "UserSettings",
                column: "Id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserSettings");
        }
    }
}
