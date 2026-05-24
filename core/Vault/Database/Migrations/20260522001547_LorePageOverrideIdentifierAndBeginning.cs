using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class LorePageOverrideIdentifierAndBeginning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "Beginning",
                table: "LoreIndexEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OverrideIdentifier",
                table: "LoreIndexEntries",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Beginning",
                table: "LoreIndexEntries");

            migrationBuilder.DropColumn(
                name: "OverrideIdentifier",
                table: "LoreIndexEntries");
        }
    }
}
