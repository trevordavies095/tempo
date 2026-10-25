using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeyCiphertextAndSingleActiveKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "KeyCiphertext",
                table: "ApiKeys",
                type: "bytea",
                nullable: true);

            // One active key per user: keep each user's newest active key, revoke the rest.
            migrationBuilder.Sql("""
                UPDATE "ApiKeys" AS k
                SET "RevokedAt" = now()
                FROM (
                    SELECT "Id",
                           ROW_NUMBER() OVER (PARTITION BY "UserId" ORDER BY "CreatedAt" DESC, "Id" DESC) AS rn
                    FROM "ApiKeys"
                    WHERE "RevokedAt" IS NULL
                ) AS ranked
                WHERE k."Id" = ranked."Id" AND ranked.rn > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_UserId_Active",
                table: "ApiKeys",
                column: "UserId",
                unique: true,
                filter: "\"RevokedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApiKeys_UserId_Active",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "KeyCiphertext",
                table: "ApiKeys");
        }
    }
}
