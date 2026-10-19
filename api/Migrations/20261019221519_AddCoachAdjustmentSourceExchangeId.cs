using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCoachAdjustmentSourceExchangeId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceExchangeId",
                table: "CoachAdjustments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoachAdjustments_SourceExchangeId",
                table: "CoachAdjustments",
                column: "SourceExchangeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CoachAdjustments_SourceExchangeId",
                table: "CoachAdjustments");

            migrationBuilder.DropColumn(
                name: "SourceExchangeId",
                table: "CoachAdjustments");
        }
    }
}
