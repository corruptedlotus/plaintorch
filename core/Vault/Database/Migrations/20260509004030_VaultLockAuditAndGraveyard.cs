using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class VaultLockAuditAndGraveyard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OccurredUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    SubjectType = table.Column<string>(type: "TEXT", nullable: true),
                    SubjectId = table.Column<string>(type: "TEXT", nullable: true),
                    SubjectTitle = table.Column<string>(type: "TEXT", nullable: true),
                    TemporalKind = table.Column<string>(type: "TEXT", nullable: true),
                    TemporalEntryKey = table.Column<string>(type: "TEXT", nullable: true),
                    TemporalEntityType = table.Column<string>(type: "TEXT", nullable: true),
                    TemporalEntityId = table.Column<string>(type: "TEXT", nullable: true),
                    TemporalEntityTitle = table.Column<string>(type: "TEXT", nullable: true),
                    TemporalLocation = table.Column<string>(type: "TEXT", nullable: true),
                    DetailsJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DatabaseGraveyardEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EntryKey = table.Column<string>(type: "TEXT", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", nullable: true),
                    EntityTitle = table.Column<string>(type: "TEXT", nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    ArchivedBy = table.Column<string>(type: "TEXT", nullable: true),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseGraveyardEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileGraveyardEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EntryKey = table.Column<string>(type: "TEXT", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", nullable: true),
                    EntityId = table.Column<string>(type: "TEXT", nullable: true),
                    EntityTitle = table.Column<string>(type: "TEXT", nullable: true),
                    OriginalRelativePath = table.Column<string>(type: "TEXT", nullable: false),
                    ArchivedRelativePath = table.Column<string>(type: "TEXT", nullable: false),
                    IsDirectory = table.Column<bool>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    ArchivedBy = table.Column<string>(type: "TEXT", nullable: true),
                    DeletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileGraveyardEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_OccurredUtc",
                table: "AuditLogEntries",
                column: "OccurredUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_SubjectType_SubjectId",
                table: "AuditLogEntries",
                columns: new[] { "SubjectType", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseGraveyardEntries_EntryKey",
                table: "DatabaseGraveyardEntries",
                column: "EntryKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileGraveyardEntries_EntryKey",
                table: "FileGraveyardEntries",
                column: "EntryKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogEntries");

            migrationBuilder.DropTable(
                name: "DatabaseGraveyardEntries");

            migrationBuilder.DropTable(
                name: "FileGraveyardEntries");
        }
    }
}
