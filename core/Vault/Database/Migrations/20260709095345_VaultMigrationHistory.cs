using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class VaultMigrationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VaultMigrationHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MigrationId = table.Column<string>(type: "TEXT", nullable: false),
                    FromVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ToVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    AppliedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    AppliedBy = table.Column<string>(type: "TEXT", nullable: true),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    EntitiesLoaded = table.Column<int>(type: "INTEGER", nullable: false),
                    FilesRewritten = table.Column<int>(type: "INTEGER", nullable: false),
                    FilesArchived = table.Column<int>(type: "INTEGER", nullable: false),
                    EntitiesImported = table.Column<int>(type: "INTEGER", nullable: false),
                    Conflicts = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VaultMigrationHistory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VaultMigrationHistory_ToVersion",
                table: "VaultMigrationHistory",
                column: "ToVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VaultMigrationHistory");
        }
    }
}
