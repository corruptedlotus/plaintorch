using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <summary>
    /// PEP103 patch 7: an item's due is a terminative, no longer an eventive. Adds the <c>Terminatives</c> table — one row
    /// at most per objective, stellar directive or checkpoint, exactly one owner set — and leaves eventives to fates alone,
    /// so their <c>ObjectiveId</c> goes and their <c>FateId</c> becomes required.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each objective's hardened due eventives become its one terminative, placed on the objective's current due, as the
    /// state engine keeps it from then on: one that no longer has a due keeps none. The terminative is resolved when the
    /// objective is finished or failed (Done, Archived, Failed) and pending otherwise, and ignored when the user had resolved
    /// any of those eventives (missed, cancelled, opted out). Dependency edges naming an objective's due eventive go, since
    /// no edge names a terminative.
    /// </para>
    /// <para>
    /// The data moves before the eventives table is rebuilt. Going back down, an objective's terminative becomes an all-day
    /// due eventive again, cancelled when it was ignored; a stellar directive's or checkpoint's has no eventive to become.
    /// </para>
    /// </remarks>
    public partial class Terminatives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Terminatives",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ObjectiveId = table.Column<string>(type: "TEXT", nullable: true),
                    DirectiveId = table.Column<string>(type: "TEXT", nullable: true),
                    CheckpointId = table.Column<string>(type: "TEXT", nullable: true),
                    Resolution = table.Column<string>(type: "TEXT", nullable: false),
                    Ignored = table.Column<bool>(type: "INTEGER", nullable: false),
                    Epoch_Moment = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Epoch_Granularity = table.Column<string>(type: "TEXT", nullable: false),
                    Epoch_Duration = table.Column<string>(type: "TEXT", nullable: true),
                    Epoch_TimeZone = table.Column<string>(type: "TEXT", nullable: true),
                    RecurrenceId = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Terminatives", x => x.Id);
                    table.CheckConstraint("CK_Terminatives_SingleOwner", "(\"ObjectiveId\" IS NOT NULL) + (\"DirectiveId\" IS NOT NULL) + (\"CheckpointId\" IS NOT NULL) = 1");
                    table.ForeignKey(
                        name: "FK_Terminatives_Checkpoints_CheckpointId",
                        column: x => x.CheckpointId,
                        principalTable: "Checkpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Terminatives_Directives_DirectiveId",
                        column: x => x.DirectiveId,
                        principalTable: "Directives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Terminatives_Incentives_ObjectiveId",
                        column: x => x.ObjectiveId,
                        principalTable: "Incentives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Terminatives_CheckpointId",
                table: "Terminatives",
                column: "CheckpointId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Terminatives_DirectiveId",
                table: "Terminatives",
                column: "DirectiveId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Terminatives_ObjectiveId",
                table: "Terminatives",
                column: "ObjectiveId",
                unique: true);

            // One terminative per objective that still has a due, on that due: a whole-day due (a midnight moment) is an
            // all-day occurrence with no zone, a timed one minute-granular in its zone.
            migrationBuilder.Sql(@"
                INSERT INTO ""Terminatives"" (""ObjectiveId"", ""Resolution"", ""Ignored"", ""Epoch_Moment"", ""Epoch_Granularity"", ""Epoch_Duration"", ""Epoch_TimeZone"", ""RecurrenceId"")
                SELECT o.""Id"",
                    CASE WHEN o.""Status"" IN ('Done', 'Archived', 'Failed') THEN 'Resolved' ELSE 'Pending' END,
                    MAX(CASE WHEN e.""Resolution"" <> 'Pending' THEN 1 ELSE 0 END),
                    o.""Due_Moment"",
                    CASE WHEN time(o.""Due_Moment"") = '00:00:00' THEN 'Day' ELSE 'Minute' END,
                    NULL,
                    CASE WHEN time(o.""Due_Moment"") = '00:00:00' THEN NULL ELSE o.""Due_TimeZone"" END,
                    o.""Due_Moment""
                FROM ""Eventives"" e
                JOIN ""Incentives"" o ON o.""Id"" = e.""ObjectiveId""
                WHERE o.""Due_Moment"" IS NOT NULL
                GROUP BY o.""Id"";");

            migrationBuilder.Sql(@"
                DELETE FROM ""Dependencies""
                WHERE (""SourceKind"" = 'Eventive' AND ""SourceId"" IN (SELECT ""Id"" FROM ""Incentives"" WHERE ""Discriminator"" = 'Objective'))
                    OR (""TargetKind"" = 'Eventive' AND ""TargetId"" IN (SELECT ""Id"" FROM ""Incentives"" WHERE ""Discriminator"" = 'Objective'));");

            // Every eventive left without a fate is an objective's, moved above; none may survive the required column.
            migrationBuilder.Sql(@"DELETE FROM ""Eventives"" WHERE ""FateId"" IS NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_Eventives_Incentives_ObjectiveId",
                table: "Eventives");

            migrationBuilder.DropIndex(
                name: "IX_Eventives_ObjectiveId",
                table: "Eventives");

            migrationBuilder.DropColumn(
                name: "ObjectiveId",
                table: "Eventives");

            migrationBuilder.AlterColumn<string>(
                name: "FateId",
                table: "Eventives",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The eventives table is rebuilt here by hand, as EF would rebuild it, because EF runs its own rebuild after
            // every other operation, and the objectives' due eventives can only go back once the table takes them.
            migrationBuilder.Sql(@"
                CREATE TABLE ""ef_temp_Eventives"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Eventives"" PRIMARY KEY AUTOINCREMENT,
                    ""Epoch_Duration"" TEXT NULL,
                    ""Epoch_Granularity"" TEXT NOT NULL,
                    ""Epoch_Moment"" TEXT NOT NULL,
                    ""Epoch_TimeZone"" TEXT NULL,
                    ""FateId"" TEXT NULL,
                    ""ObjectiveId"" TEXT NULL,
                    ""RecurrenceId"" TEXT NULL,
                    ""Resolution"" TEXT NOT NULL,
                    CONSTRAINT ""FK_Eventives_Incentives_FateId"" FOREIGN KEY (""FateId"") REFERENCES ""Incentives"" (""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_Eventives_Incentives_ObjectiveId"" FOREIGN KEY (""ObjectiveId"") REFERENCES ""Incentives"" (""Id"") ON DELETE CASCADE
                );

                INSERT INTO ""ef_temp_Eventives"" (""Id"", ""Epoch_Duration"", ""Epoch_Granularity"", ""Epoch_Moment"", ""Epoch_TimeZone"", ""FateId"", ""ObjectiveId"", ""RecurrenceId"", ""Resolution"")
                SELECT ""Id"", ""Epoch_Duration"", ""Epoch_Granularity"", ""Epoch_Moment"", ""Epoch_TimeZone"", ""FateId"", NULL, ""RecurrenceId"", ""Resolution""
                FROM ""Eventives"";

                INSERT INTO ""ef_temp_Eventives"" (""ObjectiveId"", ""FateId"", ""Resolution"", ""Epoch_Moment"", ""Epoch_Granularity"", ""Epoch_Duration"", ""Epoch_TimeZone"", ""RecurrenceId"")
                SELECT ""ObjectiveId"", NULL,
                    CASE WHEN ""Ignored"" THEN 'Cancelled' ELSE 'Pending' END,
                    date(""Epoch_Moment"") || ' 00:00:00', 'Day', NULL, NULL, date(""Epoch_Moment"") || ' 00:00:00'
                FROM ""Terminatives""
                WHERE ""ObjectiveId"" IS NOT NULL;

                DROP TABLE ""Eventives"";
                ALTER TABLE ""ef_temp_Eventives"" RENAME TO ""Eventives"";
                CREATE INDEX ""IX_Eventives_FateId"" ON ""Eventives"" (""FateId"");
                CREATE INDEX ""IX_Eventives_ObjectiveId"" ON ""Eventives"" (""ObjectiveId"");");

            migrationBuilder.DropTable(
                name: "Terminatives");
        }
    }
}
