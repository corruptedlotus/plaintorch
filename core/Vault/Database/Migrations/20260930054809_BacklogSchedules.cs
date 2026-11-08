using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <summary>
    /// PEP101: introduces backlog schedules and retires the stellar directive's start and end dates in their favour.
    /// </summary>
    /// <remarks>
    /// Hand-written to keep the data (add, backfill, drop): a directive's <c>StartDate</c> becomes its planned begin
    /// and its <c>EndDate</c> its planned finish, both at midnight — a civil moment at midnight stands for the whole
    /// day — before the columns are dropped. <c>Down</c> writes the directive plans back as dates (the time of day is
    /// lost); every other schedule has no older home and goes with the table.
    /// </remarks>
    public partial class BacklogSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BacklogSchedules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    Time = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IncentiveId = table.Column<string>(type: "TEXT", nullable: true),
                    DirectiveId = table.Column<string>(type: "TEXT", nullable: true),
                    CheckpointId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BacklogSchedules", x => x.Id);
                    table.CheckConstraint("CK_BacklogSchedules_SingleOwner", "(\"IncentiveId\" IS NOT NULL) + (\"DirectiveId\" IS NOT NULL) + (\"CheckpointId\" IS NOT NULL) = 1");
                    table.ForeignKey(
                        name: "FK_BacklogSchedules_Checkpoints_CheckpointId",
                        column: x => x.CheckpointId,
                        principalTable: "Checkpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BacklogSchedules_Directives_DirectiveId",
                        column: x => x.DirectiveId,
                        principalTable: "Directives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BacklogSchedules_Incentives_IncentiveId",
                        column: x => x.IncentiveId,
                        principalTable: "Incentives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BacklogSchedules_CheckpointId_Type_State",
                table: "BacklogSchedules",
                columns: new[] { "CheckpointId", "Type", "State" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BacklogSchedules_DirectiveId_Type_State",
                table: "BacklogSchedules",
                columns: new[] { "DirectiveId", "Type", "State" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BacklogSchedules_IncentiveId_Type_State",
                table: "BacklogSchedules",
                columns: new[] { "IncentiveId", "Type", "State" },
                unique: true);

            // A date column holds `yyyy-MM-dd`; a moment column holds `yyyy-MM-dd HH:mm:ss`, so appending midnight gives
            // exactly the text EF writes for that moment. Runs before the drop below, which rebuilds the table.
            migrationBuilder.Sql(
                """
                INSERT INTO "BacklogSchedules" ("Type", "State", "Time", "DirectiveId")
                SELECT 'Plan', 'Begin', "StartDate" || ' 00:00:00', "Id" FROM "Directives" WHERE "StartDate" IS NOT NULL;
                INSERT INTO "BacklogSchedules" ("Type", "State", "Time", "DirectiveId")
                SELECT 'Plan', 'Finish', "EndDate" || ' 00:00:00', "Id" FROM "Directives" WHERE "EndDate" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "Directives");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "Directives");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "EndDate",
                table: "Directives",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                table: "Directives",
                type: "TEXT",
                nullable: true);

            // Only a stellar directive ever had the columns; its plans return as their dates.
            migrationBuilder.Sql(
                """
                UPDATE "Directives" SET
                    "StartDate" = (SELECT substr("Time", 1, 10) FROM "BacklogSchedules"
                        WHERE "DirectiveId" = "Directives"."Id" AND "Type" = 'Plan' AND "State" = 'Begin'),
                    "EndDate" = (SELECT substr("Time", 1, 10) FROM "BacklogSchedules"
                        WHERE "DirectiveId" = "Directives"."Id" AND "Type" = 'Plan' AND "State" = 'Finish')
                WHERE "Discriminator" = 'StellarDirective';
                """);

            migrationBuilder.DropTable(
                name: "BacklogSchedules");
        }
    }
}
