using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRawFileStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RawFileData",
                table: "Workouts",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawFileName",
                table: "Workouts",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawFileType",
                table: "Workouts",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RawFileData",
                table: "Workouts");

            migrationBuilder.DropColumn(
                name: "RawFileName",
                table: "Workouts");

            migrationBuilder.DropColumn(
                name: "RawFileType",
                table: "Workouts");
        }
    }
}
