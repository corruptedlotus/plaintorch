using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CelestronLedger",
                columns: table => new
                {
                    TransactionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    SourcePuck = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CelestronLedger", x => x.TransactionId);
                });

            migrationBuilder.CreateTable(
                name: "Directives",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ParentDirectiveId = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    Due = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    AlternativeLoreDirectory = table.Column<string>(type: "TEXT", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Directives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Directives_Directives_ParentDirectiveId",
                        column: x => x.ParentDirectiveId,
                        principalTable: "Directives",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LoreIndexEntries",
                columns: table => new
                {
                    Puck = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Level = table.Column<string>(type: "TEXT", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", nullable: false),
                    ParentPuck = table.Column<string>(type: "TEXT", nullable: true),
                    Era = table.Column<int>(type: "INTEGER", nullable: true),
                    Chapter = table.Column<int>(type: "INTEGER", nullable: true),
                    Act = table.Column<int>(type: "INTEGER", nullable: true),
                    Phase = table.Column<int>(type: "INTEGER", nullable: true),
                    IndexedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoreIndexEntries", x => x.Puck);
                });

            migrationBuilder.CreateTable(
                name: "OnrushSprints",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnrushSprints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PolarisCycles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Forecast_ForecastReference = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Forecast_ForecastTarget = table.Column<string>(type: "TEXT", nullable: true),
                    StartTime = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    EndTime = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolarisCycles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PuckRegistryEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Declaration = table.Column<string>(type: "TEXT", nullable: false),
                    IssuedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PuckRegistryEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PuckSequences",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    NextValue = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PuckSequences", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "TagDefinitions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Color = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Objectives",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    DirectiveId = table.Column<string>(type: "TEXT", nullable: true),
                    OnrushSprintId = table.Column<string>(type: "TEXT", nullable: true),
                    College = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CelestronValue = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Objectives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Objectives_Directives_DirectiveId",
                        column: x => x.DirectiveId,
                        principalTable: "Directives",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Objectives_OnrushSprints_OnrushSprintId",
                        column: x => x.OnrushSprintId,
                        principalTable: "OnrushSprints",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Reflective",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    PolarisCycleId = table.Column<string>(type: "TEXT", nullable: false),
                    Executed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reflective", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reflective_PolarisCycles_PolarisCycleId",
                        column: x => x.PolarisCycleId,
                        principalTable: "PolarisCycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Executive",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PolarisCycleId = table.Column<string>(type: "TEXT", nullable: false),
                    ObjectiveId = table.Column<string>(type: "TEXT", nullable: true),
                    Executed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Executive", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Executive_Objectives_ObjectiveId",
                        column: x => x.ObjectiveId,
                        principalTable: "Objectives",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Executive_PolarisCycles_PolarisCycleId",
                        column: x => x.PolarisCycleId,
                        principalTable: "PolarisCycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Directives_ParentDirectiveId",
                table: "Directives",
                column: "ParentDirectiveId");

            migrationBuilder.CreateIndex(
                name: "IX_Executive_ObjectiveId",
                table: "Executive",
                column: "ObjectiveId");

            migrationBuilder.CreateIndex(
                name: "IX_Executive_PolarisCycleId",
                table: "Executive",
                column: "PolarisCycleId");

            migrationBuilder.CreateIndex(
                name: "IX_Objectives_DirectiveId",
                table: "Objectives",
                column: "DirectiveId");

            migrationBuilder.CreateIndex(
                name: "IX_Objectives_OnrushSprintId",
                table: "Objectives",
                column: "OnrushSprintId");

            migrationBuilder.CreateIndex(
                name: "IX_Reflective_PolarisCycleId",
                table: "Reflective",
                column: "PolarisCycleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CelestronLedger");

            migrationBuilder.DropTable(
                name: "Executive");

            migrationBuilder.DropTable(
                name: "LoreIndexEntries");

            migrationBuilder.DropTable(
                name: "PuckRegistryEntries");

            migrationBuilder.DropTable(
                name: "PuckSequences");

            migrationBuilder.DropTable(
                name: "Reflective");

            migrationBuilder.DropTable(
                name: "TagDefinitions");

            migrationBuilder.DropTable(
                name: "Objectives");

            migrationBuilder.DropTable(
                name: "PolarisCycles");

            migrationBuilder.DropTable(
                name: "Directives");

            migrationBuilder.DropTable(
                name: "OnrushSprints");
        }
    }
}
