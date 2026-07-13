using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class ExecutiveOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExecutiveOrders",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    OnrushSprintId = table.Column<string>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EffectiveUntil = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutiveOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExecutiveOrders_OnrushSprints_OnrushSprintId",
                        column: x => x.OnrushSprintId,
                        principalTable: "OnrushSprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutiveOrders_OnrushSprintId",
                table: "ExecutiveOrders",
                column: "OnrushSprintId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutiveOrders");
        }
    }
}
