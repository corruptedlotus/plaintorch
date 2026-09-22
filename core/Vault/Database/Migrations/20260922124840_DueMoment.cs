using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class DueMoment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // An objective's due becomes an owned Due (Moment + TimeZone), and checkpoints gain the same owned due
            // (PEP111). Add the owned columns, fold the old date-only due into the moment at midnight (floating, no
            // zone), then drop the old column.
            migrationBuilder.AddColumn<DateTime>(
                name: "Due_Moment",
                table: "Incentives",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Due_TimeZone",
                table: "Incentives",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE ""Incentives""
                SET ""Due_Moment"" = ""Due"" || ' 00:00:00'
                WHERE ""Due"" IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "Due",
                table: "Incentives");

            migrationBuilder.AddColumn<DateTime>(
                name: "Due_Moment",
                table: "Checkpoints",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Due_TimeZone",
                table: "Checkpoints",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort reverse: the objective's date-only due comes back from the moment's date; the checkpoint due
            // and any time-of-day/zone are dropped.
            migrationBuilder.AddColumn<DateOnly>(
                name: "Due",
                table: "Incentives",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE ""Incentives""
                SET ""Due"" = date(""Due_Moment"")
                WHERE ""Due_Moment"" IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "Due_Moment",
                table: "Incentives");

            migrationBuilder.DropColumn(
                name: "Due_TimeZone",
                table: "Incentives");

            migrationBuilder.DropColumn(
                name: "Due_Moment",
                table: "Checkpoints");

            migrationBuilder.DropColumn(
                name: "Due_TimeZone",
                table: "Checkpoints");
        }
    }
}
