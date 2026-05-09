using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class EnduringObjectivesAndExecutiveTitles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsEnduring",
                table: "Objectives",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Executive",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Codename",
                table: "Directives",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Directives_Codename",
                table: "Directives",
                column: "Codename",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Directives_Codename",
                table: "Directives");

            migrationBuilder.DropColumn(
                name: "IsEnduring",
                table: "Objectives");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "Executive");

            migrationBuilder.DropColumn(
                name: "Codename",
                table: "Directives");
        }
    }
}
