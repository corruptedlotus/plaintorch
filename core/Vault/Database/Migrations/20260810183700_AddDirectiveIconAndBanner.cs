using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectiveIconAndBanner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Banner",
                table: "Directives",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Icon",
                table: "Directives",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Banner",
                table: "Directives");

            migrationBuilder.DropColumn(
                name: "Icon",
                table: "Directives");
        }
    }
}
