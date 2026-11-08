using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <summary>
    /// Makes an eventive's RECURRENCE-ID optional, so a fate can hold <b>manual</b> eventives: occurrences added by
    /// hand rather than generated from its orbit, which belong to no recurrence set and are addressed by row id.
    /// Every existing eventive keeps its RECURRENCE-ID; the attentive column stays required.
    /// </summary>
    /// <remarks>
    /// SQLite cannot relax a column in place, so EF rebuilds the table and copies every row. Going back down, a manual
    /// eventive is kept rather than dropped: its current moment is pinned as its RECURRENCE-ID first, the same slot a
    /// schedule-born occurrence pins when it materializes.
    /// </remarks>
    public partial class ManualEventives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "RecurrenceId",
                table: "Eventives",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "TEXT");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""Eventives""
                SET ""RecurrenceId"" = ""Epoch_Moment""
                WHERE ""RecurrenceId"" IS NULL;");

            migrationBuilder.AlterColumn<DateTime>(
                name: "RecurrenceId",
                table: "Eventives",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
