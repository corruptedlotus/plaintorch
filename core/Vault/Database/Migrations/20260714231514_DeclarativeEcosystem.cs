using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class DeclarativeEcosystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Executive_Objectives_ObjectiveId",
                table: "Executive");

            migrationBuilder.DropForeignKey(
                name: "FK_Objectives_Directives_DirectiveId",
                table: "Objectives");

            migrationBuilder.DropForeignKey(
                name: "FK_Objectives_OnrushSprints_OnrushSprintId",
                table: "Objectives");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Objectives",
                table: "Objectives");

            migrationBuilder.RenameTable(
                name: "Objectives",
                newName: "Incentives");

            migrationBuilder.RenameIndex(
                name: "IX_Objectives_OnrushSprintId",
                table: "Incentives",
                newName: "IX_Incentives_OnrushSprintId");

            migrationBuilder.RenameIndex(
                name: "IX_Objectives_DirectiveId",
                table: "Incentives",
                newName: "IX_Incentives_DirectiveId");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "Time",
                table: "Reflective",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AffinityTimeframeId",
                table: "Executive",
                type: "INTEGER",
                nullable: true);

            // Existing rows are classic (stellar) directives; the backfilled default keeps them queryable
            // under the new TPH discriminator.
            migrationBuilder.AddColumn<string>(
                name: "Discriminator",
                table: "Directives",
                type: "TEXT",
                maxLength: 21,
                nullable: false,
                defaultValue: "Directive");

            migrationBuilder.AddColumn<string>(
                name: "LunarStatus",
                table: "Directives",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Incentives",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnduring",
                table: "Incentives",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<string>(
                name: "College",
                table: "Incentives",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<int>(
                name: "CelestronValue",
                table: "Incentives",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "ActiveCelestron",
                table: "Incentives",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "Date",
                table: "Incentives",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecreeStatus",
                table: "Incentives",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultLength",
                table: "Incentives",
                type: "INTEGER",
                nullable: true);

            // Every pre-PEP100 incentive row is an objective; the backfilled default keeps them queryable
            // under the new TPH discriminator.
            migrationBuilder.AddColumn<string>(
                name: "Discriminator",
                table: "Incentives",
                type: "TEXT",
                maxLength: 13,
                nullable: false,
                defaultValue: "Objective");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "EndTime",
                table: "Incentives",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EventDuration",
                table: "Incentives",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FateStatus",
                table: "Incentives",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Orbit",
                table: "Incentives",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParentIncentiveId",
                table: "Incentives",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Reflect",
                table: "Incentives",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "StartTime",
                table: "Incentives",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Incentives",
                table: "Incentives",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "Attentives",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DecreeId = table.Column<string>(type: "TEXT", nullable: false),
                    PolarisCycleId = table.Column<string>(type: "TEXT", nullable: true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Time = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                    Resolution = table.Column<string>(type: "TEXT", nullable: false),
                    Estimation = table.Column<int>(type: "INTEGER", nullable: true),
                    Minimum = table.Column<int>(type: "INTEGER", nullable: true),
                    Maximum = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attentives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Attentives_Incentives_DecreeId",
                        column: x => x.DecreeId,
                        principalTable: "Incentives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Attentives_PolarisCycles_PolarisCycleId",
                        column: x => x.PolarisCycleId,
                        principalTable: "PolarisCycles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Eventives",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FateId = table.Column<string>(type: "TEXT", nullable: true),
                    ObjectiveId = table.Column<string>(type: "TEXT", nullable: true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                    EndTime = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                    Resolution = table.Column<string>(type: "TEXT", nullable: false),
                    Estimation = table.Column<int>(type: "INTEGER", nullable: true),
                    Minimum = table.Column<int>(type: "INTEGER", nullable: true),
                    Maximum = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Eventives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Eventives_Incentives_FateId",
                        column: x => x.FateId,
                        principalTable: "Incentives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Eventives_Incentives_ObjectiveId",
                        column: x => x.ObjectiveId,
                        principalTable: "Incentives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Timeframes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DirectiveId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    Orbit = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Timeframes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Timeframes_Directives_DirectiveId",
                        column: x => x.DirectiveId,
                        principalTable: "Directives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Executive_AffinityTimeframeId",
                table: "Executive",
                column: "AffinityTimeframeId");

            migrationBuilder.CreateIndex(
                name: "IX_Incentives_ParentIncentiveId",
                table: "Incentives",
                column: "ParentIncentiveId");

            migrationBuilder.CreateIndex(
                name: "IX_Attentives_DecreeId",
                table: "Attentives",
                column: "DecreeId");

            migrationBuilder.CreateIndex(
                name: "IX_Attentives_PolarisCycleId",
                table: "Attentives",
                column: "PolarisCycleId");

            migrationBuilder.CreateIndex(
                name: "IX_Eventives_FateId",
                table: "Eventives",
                column: "FateId");

            migrationBuilder.CreateIndex(
                name: "IX_Eventives_ObjectiveId",
                table: "Eventives",
                column: "ObjectiveId");

            migrationBuilder.CreateIndex(
                name: "IX_Timeframes_DirectiveId",
                table: "Timeframes",
                column: "DirectiveId");

            migrationBuilder.AddForeignKey(
                name: "FK_Executive_Incentives_ObjectiveId",
                table: "Executive",
                column: "ObjectiveId",
                principalTable: "Incentives",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Executive_Timeframes_AffinityTimeframeId",
                table: "Executive",
                column: "AffinityTimeframeId",
                principalTable: "Timeframes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Incentives_Directives_DirectiveId",
                table: "Incentives",
                column: "DirectiveId",
                principalTable: "Directives",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Incentives_Incentives_ParentIncentiveId",
                table: "Incentives",
                column: "ParentIncentiveId",
                principalTable: "Incentives",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Incentives_OnrushSprints_OnrushSprintId",
                table: "Incentives",
                column: "OnrushSprintId",
                principalTable: "OnrushSprints",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Executive_Incentives_ObjectiveId",
                table: "Executive");

            migrationBuilder.DropForeignKey(
                name: "FK_Executive_Timeframes_AffinityTimeframeId",
                table: "Executive");

            migrationBuilder.DropForeignKey(
                name: "FK_Incentives_Directives_DirectiveId",
                table: "Incentives");

            migrationBuilder.DropForeignKey(
                name: "FK_Incentives_Incentives_ParentIncentiveId",
                table: "Incentives");

            migrationBuilder.DropForeignKey(
                name: "FK_Incentives_OnrushSprints_OnrushSprintId",
                table: "Incentives");

            migrationBuilder.DropTable(
                name: "Attentives");

            migrationBuilder.DropTable(
                name: "Eventives");

            migrationBuilder.DropTable(
                name: "Timeframes");

            migrationBuilder.DropIndex(
                name: "IX_Executive_AffinityTimeframeId",
                table: "Executive");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Incentives",
                table: "Incentives");

            migrationBuilder.DropIndex(
                name: "IX_Incentives_ParentIncentiveId",
                table: "Incentives");

            migrationBuilder.DropColumn(
                name: "Time",
                table: "Reflective");

            migrationBuilder.DropColumn(
                name: "AffinityTimeframeId",
                table: "Executive");

            migrationBuilder.DropColumn(
                name: "Discriminator",
                table: "Directives");

            migrationBuilder.DropColumn(
                name: "LunarStatus",
                table: "Directives");

            migrationBuilder.DropColumn(
                name: "ActiveCelestron",
                table: "Incentives");

            migrationBuilder.DropColumn(
                name: "Date",
                table: "Incentives");

            migrationBuilder.DropColumn(
                name: "DecreeStatus",
                table: "Incentives");

            migrationBuilder.DropColumn(
                name: "DefaultLength",
                table: "Incentives");

            migrationBuilder.DropColumn(
                name: "Discriminator",
                table: "Incentives");

            migrationBuilder.DropColumn(
                name: "EndTime",
                table: "Incentives");

            migrationBuilder.DropColumn(
                name: "EventDuration",
                table: "Incentives");

            migrationBuilder.DropColumn(
                name: "FateStatus",
                table: "Incentives");

            migrationBuilder.DropColumn(
                name: "Orbit",
                table: "Incentives");

            migrationBuilder.DropColumn(
                name: "ParentIncentiveId",
                table: "Incentives");

            migrationBuilder.DropColumn(
                name: "Reflect",
                table: "Incentives");

            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "Incentives");

            migrationBuilder.RenameTable(
                name: "Incentives",
                newName: "Objectives");

            migrationBuilder.RenameIndex(
                name: "IX_Incentives_OnrushSprintId",
                table: "Objectives",
                newName: "IX_Objectives_OnrushSprintId");

            migrationBuilder.RenameIndex(
                name: "IX_Incentives_DirectiveId",
                table: "Objectives",
                newName: "IX_Objectives_DirectiveId");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Objectives",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnduring",
                table: "Objectives",
                type: "INTEGER",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "College",
                table: "Objectives",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "CelestronValue",
                table: "Objectives",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Objectives",
                table: "Objectives",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Executive_Objectives_ObjectiveId",
                table: "Executive",
                column: "ObjectiveId",
                principalTable: "Objectives",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Objectives_Directives_DirectiveId",
                table: "Objectives",
                column: "DirectiveId",
                principalTable: "Directives",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Objectives_OnrushSprints_OnrushSprintId",
                table: "Objectives",
                column: "OnrushSprintId",
                principalTable: "OnrushSprints",
                principalColumn: "Id");
        }
    }
}
