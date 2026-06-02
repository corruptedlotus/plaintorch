using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddObjectiveDueAndRemoveExecutiveTitle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Title",
                table: "Executive");

            migrationBuilder.AddColumn<DateOnly>(
                name: "Due",
                table: "Objectives",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Due",
                table: "Objectives");

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Executive",
                type: "TEXT",
                nullable: true);
        }
    }
}
