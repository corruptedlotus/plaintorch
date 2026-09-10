using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class AttentiveRecurrenceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "RecurrenceDate",
                table: "Attentives",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "RecurrenceTime",
                table: "Attentives",
                type: "TEXT",
                nullable: true);

            // Backfill the RECURRENCE-ID of existing attentives from their current slot: legacy rows have no
            // record of an original slot, so their present Date/Time is the best available identity (and is
            // exact for any that were never rescheduled).
            migrationBuilder.Sql("UPDATE \"Attentives\" SET \"RecurrenceDate\" = \"Date\", \"RecurrenceTime\" = \"Time\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecurrenceDate",
                table: "Attentives");

            migrationBuilder.DropColumn(
                name: "RecurrenceTime",
                table: "Attentives");
        }
    }
}
