using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class LunarStellarDirectiveSiblings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PEP100: the classic concrete "Directive" became the abstract base of the stellar/lunar siblings.
            // Legacy rows written under the old concrete discriminator become stellar directives. Run this before
            // the column rebuild below so it operates on the stable pre-rebuild table.
            migrationBuilder.Sql("UPDATE \"Directives\" SET \"Discriminator\" = 'StellarDirective' WHERE \"Discriminator\" = 'Directive';");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Directives",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Directives\" SET \"Discriminator\" = 'Directive' WHERE \"Discriminator\" = 'StellarDirective';");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Directives",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
