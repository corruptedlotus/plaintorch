using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <summary>
    /// Collapses every occurrence slot into one RECURRENCE-ID moment (PEP111): an occurrence's
    /// <c>RecurrenceDate</c> + <c>RecurrenceTime</c> become a single <c>RecurrenceId</c> datetime, as do both
    /// slots of a dependency edge; and an attentive's <c>PeriodEndDate</c> folds into its epoch duration, which
    /// already describes how long the occurrence lasts.
    /// </summary>
    /// <remarks>
    /// Hand-written add → backfill → drop, so no slot or period is lost; the scaffolded version guessed renames
    /// that would have dropped every time of day and moved dependency target slots into the source column.
    /// Backfilled moments use the <c>yyyy-MM-dd HH:mm:ss</c> text form EF writes, so equality lookups on the new
    /// columns match rows migrated here exactly as they match rows written by the model.
    /// </remarks>
    public partial class OccurrenceRecurrenceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RecurrenceId",
                table: "Attentives",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "RecurrenceId",
                table: "Eventives",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "SourceRecurrenceId",
                table: "Dependencies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TargetRecurrenceId",
                table: "Dependencies",
                type: "TEXT",
                nullable: true);

            // An all-day slot (no time) is the midnight of its date.
            migrationBuilder.Sql(@"
                UPDATE ""Attentives"" SET
                    ""RecurrenceId"" = ""RecurrenceDate"" || ' ' || COALESCE(""RecurrenceTime"", '00:00:00');");

            migrationBuilder.Sql(@"
                UPDATE ""Eventives"" SET
                    ""RecurrenceId"" = ""RecurrenceDate"" || ' ' || COALESCE(""RecurrenceTime"", '00:00:00');");

            migrationBuilder.Sql(@"
                UPDATE ""Dependencies"" SET
                    ""SourceRecurrenceId"" = CASE WHEN ""SourceRecurrenceDate"" IS NULL THEN NULL
                        ELSE ""SourceRecurrenceDate"" || ' ' || COALESCE(""SourceRecurrenceTime"", '00:00:00') END,
                    ""TargetRecurrenceId"" = CASE WHEN ""TargetRecurrenceDate"" IS NULL THEN NULL
                        ELSE ""TargetRecurrenceDate"" || ' ' || COALESCE(""TargetRecurrenceTime"", '00:00:00') END;");

            // A super-day attentive's period ran [RecurrenceDate, PeriodEndDate), resolved on its decree's calendar;
            // it becomes the epoch duration in whole days (the form Epoch.For writes), so a Pleiadean month keeps
            // its 60/61 days rather than falling back to a nominal Gregorian month.
            migrationBuilder.Sql(@"
                UPDATE ""Attentives"" SET
                    ""Epoch_Duration"" = CAST(julianday(""PeriodEndDate"") - julianday(""RecurrenceDate"") AS INTEGER) || 'd'
                WHERE ""PeriodEndDate"" IS NOT NULL AND ""Epoch_Duration"" IS NULL;");

            migrationBuilder.DropIndex(
                name: "IX_Dependencies_SourceKind_SourceId_SourceRecurrenceDate_SourceRecurrenceTime_TargetKind_TargetId_TargetRecurrenceDate_TargetRecurrenceTime_Trigger_Constraint",
                table: "Dependencies");

            migrationBuilder.DropColumn(
                name: "RecurrenceDate",
                table: "Attentives");

            migrationBuilder.DropColumn(
                name: "RecurrenceTime",
                table: "Attentives");

            migrationBuilder.DropColumn(
                name: "PeriodEndDate",
                table: "Attentives");

            migrationBuilder.DropColumn(
                name: "RecurrenceDate",
                table: "Eventives");

            migrationBuilder.DropColumn(
                name: "RecurrenceTime",
                table: "Eventives");

            migrationBuilder.DropColumn(
                name: "SourceRecurrenceDate",
                table: "Dependencies");

            migrationBuilder.DropColumn(
                name: "SourceRecurrenceTime",
                table: "Dependencies");

            migrationBuilder.DropColumn(
                name: "TargetRecurrenceDate",
                table: "Dependencies");

            migrationBuilder.DropColumn(
                name: "TargetRecurrenceTime",
                table: "Dependencies");

            migrationBuilder.CreateIndex(
                name: "IX_Dependencies_SourceKind_SourceId_SourceRecurrenceId_TargetKind_TargetId_TargetRecurrenceId_Trigger_Constraint",
                table: "Dependencies",
                columns: new[] { "SourceKind", "SourceId", "SourceRecurrenceId", "TargetKind", "TargetId", "TargetRecurrenceId", "Trigger", "Constraint" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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

            migrationBuilder.AddColumn<DateOnly>(
                name: "PeriodEndDate",
                table: "Attentives",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RecurrenceDate",
                table: "Eventives",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "RecurrenceTime",
                table: "Eventives",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SourceRecurrenceDate",
                table: "Dependencies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "SourceRecurrenceTime",
                table: "Dependencies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "TargetRecurrenceDate",
                table: "Dependencies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "TargetRecurrenceTime",
                table: "Dependencies",
                type: "TEXT",
                nullable: true);

            // A midnight slot splits back into an all-day (time-less) slot; a timed one keeps its time.
            migrationBuilder.Sql(@"
                UPDATE ""Attentives"" SET
                    ""RecurrenceDate"" = date(""RecurrenceId""),
                    ""RecurrenceTime"" = CASE WHEN time(""RecurrenceId"") = '00:00:00' THEN NULL ELSE time(""RecurrenceId"") END;");

            migrationBuilder.Sql(@"
                UPDATE ""Eventives"" SET
                    ""RecurrenceDate"" = date(""RecurrenceId""),
                    ""RecurrenceTime"" = CASE WHEN time(""RecurrenceId"") = '00:00:00' THEN NULL ELSE time(""RecurrenceId"") END;");

            migrationBuilder.Sql(@"
                UPDATE ""Dependencies"" SET
                    ""SourceRecurrenceDate"" = date(""SourceRecurrenceId""),
                    ""SourceRecurrenceTime"" = CASE WHEN ""SourceRecurrenceId"" IS NULL OR time(""SourceRecurrenceId"") = '00:00:00' THEN NULL ELSE time(""SourceRecurrenceId"") END,
                    ""TargetRecurrenceDate"" = date(""TargetRecurrenceId""),
                    ""TargetRecurrenceTime"" = CASE WHEN ""TargetRecurrenceId"" IS NULL OR time(""TargetRecurrenceId"") = '00:00:00' THEN NULL ELSE time(""TargetRecurrenceId"") END;");

            // A whole-days duration longer than a day was a super-day period; it moves back to PeriodEndDate.
            migrationBuilder.Sql(@"
                UPDATE ""Attentives"" SET
                    ""PeriodEndDate"" = date(""RecurrenceDate"", '+' || CAST(REPLACE(""Epoch_Duration"", 'd', '') AS INTEGER) || ' days'),
                    ""Epoch_Duration"" = NULL
                WHERE ""Epoch_Duration"" GLOB '[0-9]*d'
                    AND ""Epoch_Duration"" NOT GLOB '*[^0-9d]*'
                    AND CAST(REPLACE(""Epoch_Duration"", 'd', '') AS INTEGER) > 1;");

            migrationBuilder.DropIndex(
                name: "IX_Dependencies_SourceKind_SourceId_SourceRecurrenceId_TargetKind_TargetId_TargetRecurrenceId_Trigger_Constraint",
                table: "Dependencies");

            migrationBuilder.DropColumn(
                name: "RecurrenceId",
                table: "Attentives");

            migrationBuilder.DropColumn(
                name: "RecurrenceId",
                table: "Eventives");

            migrationBuilder.DropColumn(
                name: "SourceRecurrenceId",
                table: "Dependencies");

            migrationBuilder.DropColumn(
                name: "TargetRecurrenceId",
                table: "Dependencies");

            migrationBuilder.CreateIndex(
                name: "IX_Dependencies_SourceKind_SourceId_SourceRecurrenceDate_SourceRecurrenceTime_TargetKind_TargetId_TargetRecurrenceDate_TargetRecurrenceTime_Trigger_Constraint",
                table: "Dependencies",
                columns: new[] { "SourceKind", "SourceId", "SourceRecurrenceDate", "SourceRecurrenceTime", "TargetKind", "TargetId", "TargetRecurrenceDate", "TargetRecurrenceTime", "Trigger", "Constraint" },
                unique: true);
        }
    }
}
