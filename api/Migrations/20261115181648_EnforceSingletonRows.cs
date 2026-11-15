using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <summary>
    /// UserSettings and IntervalsIcuConnections are one-row tables. Keeps the most recently updated
    /// row in each (ties: lowest Id, matching SingletonRows.SingleRowAsync), deletes the rest, then
    /// adds a unique index on a constant so Postgres rejects a second row. DatabaseMigrationHelper
    /// logs how many rows will be removed before this runs.
    /// </summary>
    public partial class EnforceSingletonRows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "UserSettings"
                WHERE "Id" <> (
                    SELECT "Id" FROM "UserSettings"
                    ORDER BY "UpdatedAt" DESC, "Id"
                    LIMIT 1);

                CREATE UNIQUE INDEX "IX_UserSettings_Singleton" ON "UserSettings" ((true));

                DELETE FROM "IntervalsIcuConnections"
                WHERE "Id" <> (
                    SELECT "Id" FROM "IntervalsIcuConnections"
                    ORDER BY "UpdatedAt" DESC, "Id"
                    LIMIT 1);

                CREATE UNIQUE INDEX "IX_IntervalsIcuConnections_Singleton" ON "IntervalsIcuConnections" ((true));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Removed duplicate rows are not restored.
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_IntervalsIcuConnections_Singleton";
                DROP INDEX IF EXISTS "IX_UserSettings_Singleton";
                """);
        }
    }
}
