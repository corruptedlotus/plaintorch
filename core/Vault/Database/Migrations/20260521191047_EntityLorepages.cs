using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class EntityLorepages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_LoreIndexEntries_ParentPuck",
                table: "LoreIndexEntries",
                column: "ParentPuck");

            migrationBuilder.AddForeignKey(
                name: "FK_LoreIndexEntries_LoreIndexEntries_ParentPuck",
                table: "LoreIndexEntries",
                column: "ParentPuck",
                principalTable: "LoreIndexEntries",
                principalColumn: "Puck");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LoreIndexEntries_LoreIndexEntries_ParentPuck",
                table: "LoreIndexEntries");

            migrationBuilder.DropIndex(
                name: "IX_LoreIndexEntries_ParentPuck",
                table: "LoreIndexEntries");
        }
    }
}
