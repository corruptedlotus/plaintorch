using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class FateOrbitOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A fate is now stored orbit-only (PEP111): its one-off Date/StartTime/EndTime/EventDuration fold into a
            // fixed-datetime Z{…} literal before the columns are dropped. An all-day one-off becomes a date-only
            // Z{y/M/d}; a timed one-off becomes Z{y/M/dTh:m}, with a =<dur>m span from its end-time window (else its
            // event duration). Only fate rows carry a Date, so the fold is naturally fate-scoped.
            migrationBuilder.Sql(@"
                UPDATE ""Incentives""
                SET ""Orbit"" = CASE
                    WHEN ""StartTime"" IS NULL
                        THEN 'Z{' || substr(""Date"", 1, 4) || '/' || substr(""Date"", 6, 2) || '/' || substr(""Date"", 9, 2) || '}'
                    ELSE
                        'Z{' || substr(""Date"", 1, 4) || '/' || substr(""Date"", 6, 2) || '/' || substr(""Date"", 9, 2)
                             || 'T' || substr(""StartTime"", 1, 2) || ':' || substr(""StartTime"", 4, 2) || '}'
                             || CASE
                                 WHEN ""EndTime"" IS NOT NULL
                                     AND (strftime('%s', '2000-01-01 ' || ""EndTime"") - strftime('%s', '2000-01-01 ' || ""StartTime"")) > 0
                                     THEN '=' || CAST((strftime('%s', '2000-01-01 ' || ""EndTime"") - strftime('%s', '2000-01-01 ' || ""StartTime"")) / 60 AS INTEGER) || 'm'
                                 WHEN ""EndTime"" IS NULL AND ""EventDuration"" IS NOT NULL AND ""EventDuration"" > 0
                                     THEN '=' || ""EventDuration"" || 'm'
                                 ELSE ''
                                END
                    END
                WHERE ""Orbit"" IS NULL AND ""Date"" IS NOT NULL;");

            // Pin existing fates to the Gregorian calendar so their current resolution is preserved if the kind
            // default ever changes (PEP116). Scoped to the fate discriminator so decrees keep their Pleiadean default.
            migrationBuilder.Sql(@"
                UPDATE ""Incentives""
                SET ""Calendar"" = 'Gregorian'
                WHERE ""Discriminator"" = 'Fate' AND ""Calendar"" IS NULL;");

            migrationBuilder.DropColumn(
                name: "Date",
                table: "Incentives");

            migrationBuilder.DropColumn(
                name: "EndTime",
                table: "Incentives");

            migrationBuilder.DropColumn(
                name: "EventDuration",
                table: "Incentives");

            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "Incentives");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort reverse: the one-off columns come back, but the fold into the Z{…} orbit is not undone —
            // the schedule lives on in Orbit, so the columns come back empty rather than reconstructed by parsing the
            // literal. The calendar pin is likewise left in place.
            migrationBuilder.AddColumn<DateOnly>(
                name: "Date",
                table: "Incentives",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "EndTime",
                table: "Incentives",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EventDuration",
                table: "Incentives",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "StartTime",
                table: "Incentives",
                type: "TEXT",
                nullable: true);
        }
    }
}
