using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationStatusEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperationStatusEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OccurredUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Transition = table.Column<string>(type: "TEXT", nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", nullable: false),
                    ScopeKey = table.Column<string>(type: "TEXT", nullable: false),
                    ReasonCode = table.Column<string>(type: "TEXT", nullable: false),
                    Severity = table.Column<string>(type: "TEXT", nullable: false),
                    PreviousSeverity = table.Column<string>(type: "TEXT", nullable: true),
                    FilesJson = table.Column<string>(type: "TEXT", nullable: true),
                    EntityId = table.Column<string>(type: "TEXT", nullable: true),
                    Detail = table.Column<string>(type: "TEXT", nullable: true),
                    OccurrenceCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationStatusEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperationStatusEvents_OccurredUtc",
                table: "OperationStatusEvents",
                column: "OccurredUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OperationStatusEvents_OperationId_ScopeKey",
                table: "OperationStatusEvents",
                columns: new[] { "OperationId", "ScopeKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperationStatusEvents");
        }
    }
}
