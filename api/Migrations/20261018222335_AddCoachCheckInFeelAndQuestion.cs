using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCoachCheckInFeelAndQuestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "FeelScore",
                table: "CoachCheckIns",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuestionForCoach",
                table: "CoachCheckIns",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FeelScore",
                table: "CoachCheckIns");

            migrationBuilder.DropColumn(
                name: "QuestionForCoach",
                table: "CoachCheckIns");
        }
    }
}
