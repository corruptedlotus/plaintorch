using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class ExecutiveIncentiveId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Executive_Incentives_ObjectiveId",
                table: "Executive");

            migrationBuilder.DropIndex(
                name: "IX_Executive_PolarisCycleId_ObjectiveId",
                table: "Executive");

            migrationBuilder.RenameColumn(
                name: "ObjectiveId",
                table: "Executive",
                newName: "IncentiveId");

            migrationBuilder.RenameIndex(
                name: "IX_Executive_ObjectiveId",
                table: "Executive",
                newName: "IX_Executive_IncentiveId");

            migrationBuilder.CreateIndex(
                name: "IX_Executive_PolarisCycleId_IncentiveId",
                table: "Executive",
                columns: new[] { "PolarisCycleId", "IncentiveId" },
                unique: true,
                filter: "\"IncentiveId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Executive_Incentives_IncentiveId",
                table: "Executive",
                column: "IncentiveId",
                principalTable: "Incentives",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Executive_Incentives_IncentiveId",
                table: "Executive");

            migrationBuilder.DropIndex(
                name: "IX_Executive_PolarisCycleId_IncentiveId",
                table: "Executive");

            migrationBuilder.RenameColumn(
                name: "IncentiveId",
                table: "Executive",
                newName: "ObjectiveId");

            migrationBuilder.RenameIndex(
                name: "IX_Executive_IncentiveId",
                table: "Executive",
                newName: "IX_Executive_ObjectiveId");

            migrationBuilder.CreateIndex(
                name: "IX_Executive_PolarisCycleId_ObjectiveId",
                table: "Executive",
                columns: new[] { "PolarisCycleId", "ObjectiveId" },
                unique: true,
                filter: "\"ObjectiveId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Executive_Incentives_ObjectiveId",
                table: "Executive",
                column: "ObjectiveId",
                principalTable: "Incentives",
                principalColumn: "Id");
        }
    }
}
