using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tempo.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Workouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationS = table.Column<int>(type: "integer", nullable: false),
                    DistanceM = table.Column<double>(type: "double precision", nullable: false),
                    AvgPaceS = table.Column<int>(type: "integer", nullable: false),
                    ElevGainM = table.Column<double>(type: "double precision", nullable: true),
                    ElevLossM = table.Column<double>(type: "double precision", nullable: true),
                    MinElevM = table.Column<double>(type: "double precision", nullable: true),
                    MaxElevM = table.Column<double>(type: "double precision", nullable: true),
                    MaxSpeedMps = table.Column<double>(type: "double precision", nullable: true),
                    AvgSpeedMps = table.Column<double>(type: "double precision", nullable: true),
                    MovingTimeS = table.Column<int>(type: "integer", nullable: true),
                    MaxHeartRateBpm = table.Column<byte>(type: "smallint", nullable: true),
                    AvgHeartRateBpm = table.Column<byte>(type: "smallint", nullable: true),
                    MinHeartRateBpm = table.Column<byte>(type: "smallint", nullable: true),
                    MaxCadenceRpm = table.Column<byte>(type: "smallint", nullable: true),
                    AvgCadenceRpm = table.Column<byte>(type: "smallint", nullable: true),
                    MaxPowerWatts = table.Column<int>(type: "integer", nullable: true),
                    AvgPowerWatts = table.Column<int>(type: "integer", nullable: true),
                    Calories = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "text", maxLength: 200, nullable: true),
                    RunType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawGpxData = table.Column<string>(type: "jsonb", nullable: true),
                    RawFitData = table.Column<string>(type: "jsonb", nullable: true),
                    RawStravaData = table.Column<string>(type: "jsonb", nullable: true),
                    Weather = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workouts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkoutMedia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    Filename = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FilePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    MimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Caption = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutMedia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkoutMedia_Workouts_WorkoutId",
                        column: x => x.WorkoutId,
                        principalTable: "Workouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkoutRoutes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    RouteGeoJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkoutRoutes_Workouts_WorkoutId",
                        column: x => x.WorkoutId,
                        principalTable: "Workouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkoutSplits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    Idx = table.Column<int>(type: "integer", nullable: false),
                    DistanceM = table.Column<double>(type: "double precision", nullable: false),
                    DurationS = table.Column<int>(type: "integer", nullable: false),
                    PaceS = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutSplits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkoutSplits_Workouts_WorkoutId",
                        column: x => x.WorkoutId,
                        principalTable: "Workouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkoutTimeSeries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    ElapsedSeconds = table.Column<int>(type: "integer", nullable: false),
                    DistanceM = table.Column<double>(type: "double precision", nullable: true),
                    HeartRateBpm = table.Column<byte>(type: "smallint", nullable: true),
                    CadenceRpm = table.Column<byte>(type: "smallint", nullable: true),
                    PowerWatts = table.Column<int>(type: "integer", nullable: true),
                    SpeedMps = table.Column<double>(type: "double precision", nullable: true),
                    GradePercent = table.Column<double>(type: "double precision", nullable: true),
                    ElevationM = table.Column<double>(type: "double precision", nullable: true),
                    TemperatureC = table.Column<short>(type: "smallint", nullable: true),
                    VerticalSpeedMps = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutTimeSeries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkoutTimeSeries_Workouts_WorkoutId",
                        column: x => x.WorkoutId,
                        principalTable: "Workouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutMedia_WorkoutId",
                table: "WorkoutMedia",
                column: "WorkoutId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutRoutes_WorkoutId",
                table: "WorkoutRoutes",
                column: "WorkoutId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Workouts_RawFitData",
                table: "Workouts",
                column: "RawFitData")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_Workouts_RawGpxData",
                table: "Workouts",
                column: "RawGpxData")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_Workouts_RawStravaData",
                table: "Workouts",
                column: "RawStravaData")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_Workouts_RunType",
                table: "Workouts",
                column: "RunType");

            migrationBuilder.CreateIndex(
                name: "IX_Workouts_Source",
                table: "Workouts",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_Workouts_StartedAt",
                table: "Workouts",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Workouts_StartedAt_DistanceM_DurationS",
                table: "Workouts",
                columns: new[] { "StartedAt", "DistanceM", "DurationS" });

            migrationBuilder.CreateIndex(
                name: "IX_Workouts_Weather",
                table: "Workouts",
                column: "Weather")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSplits_WorkoutId_Idx",
                table: "WorkoutSplits",
                columns: new[] { "WorkoutId", "Idx" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutTimeSeries_WorkoutId_ElapsedSeconds",
                table: "WorkoutTimeSeries",
                columns: new[] { "WorkoutId", "ElapsedSeconds" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkoutMedia");

            migrationBuilder.DropTable(
                name: "WorkoutRoutes");

            migrationBuilder.DropTable(
                name: "WorkoutSplits");

            migrationBuilder.DropTable(
                name: "WorkoutTimeSeries");

            migrationBuilder.DropTable(
                name: "Workouts");
        }
    }
}
