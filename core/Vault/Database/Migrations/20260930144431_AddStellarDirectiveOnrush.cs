using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddStellarDirectiveOnrush : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OnrushSprintId",
                table: "Directives",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Directives_OnrushSprintId",
                table: "Directives",
                column: "OnrushSprintId");

            migrationBuilder.AddForeignKey(
                name: "FK_Directives_OnrushSprints_OnrushSprintId",
                table: "Directives",
                column: "OnrushSprintId",
                principalTable: "OnrushSprints",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Directives_OnrushSprints_OnrushSprintId",
                table: "Directives");

            migrationBuilder.DropIndex(
                name: "IX_Directives_OnrushSprintId",
                table: "Directives");

            migrationBuilder.DropColumn(
                name: "OnrushSprintId",
                table: "Directives");
        }
    }
}
