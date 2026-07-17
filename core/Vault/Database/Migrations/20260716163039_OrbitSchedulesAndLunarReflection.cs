using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class OrbitSchedulesAndLunarReflection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DecreeId",
                table: "Reflective",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PeriodEndDate",
                table: "Attentives",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrbitScheduleStates",
                columns: table => new
                {
                    IncentiveId = table.Column<string>(type: "TEXT", nullable: false),
                    StateJson = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrbitScheduleStates", x => x.IncentiveId);
                    table.ForeignKey(
                        name: "FK_OrbitScheduleStates_Incentives_IncentiveId",
                        column: x => x.IncentiveId,
                        principalTable: "Incentives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reflective_DecreeId",
                table: "Reflective",
                column: "DecreeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Reflective_Incentives_DecreeId",
                table: "Reflective",
                column: "DecreeId",
                principalTable: "Incentives",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reflective_Incentives_DecreeId",
                table: "Reflective");

            migrationBuilder.DropTable(
                name: "OrbitScheduleStates");

            migrationBuilder.DropIndex(
                name: "IX_Reflective_DecreeId",
                table: "Reflective");

            migrationBuilder.DropColumn(
                name: "DecreeId",
                table: "Reflective");

            migrationBuilder.DropColumn(
                name: "PeriodEndDate",
                table: "Attentives");
        }
    }
}
