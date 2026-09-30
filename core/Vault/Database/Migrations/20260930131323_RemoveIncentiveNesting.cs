using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class RemoveIncentiveNesting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Incentives_Incentives_ParentIncentiveId",
                table: "Incentives");

            migrationBuilder.DropIndex(
                name: "IX_Incentives_ParentIncentiveId",
                table: "Incentives");

            migrationBuilder.DropColumn(
                name: "ParentIncentiveId",
                table: "Incentives");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ParentIncentiveId",
                table: "Incentives",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Incentives_ParentIncentiveId",
                table: "Incentives",
                column: "ParentIncentiveId");

            migrationBuilder.AddForeignKey(
                name: "FK_Incentives_Incentives_ParentIncentiveId",
                table: "Incentives",
                column: "ParentIncentiveId",
                principalTable: "Incentives",
                principalColumn: "Id");
        }
    }
}
