using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class OccurrenceEpoch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Occurrences now carry an owned Epoch (Moment + Granularity + Duration + TimeZone) instead of a bare
            // Date/Time (attentives) or Date/StartTime/EndTime (eventives). Add the Epoch_* columns, fold the old
            // values in (Moment = date + time-of-day; Granularity = Minute when a time was present, else Day; an
            // eventive's StartTime/EndTime window becomes a minute-notation Duration), then drop the old columns.

            // --- Attentives ---
            migrationBuilder.AddColumn<string>(name: "Epoch_Moment", table: "Attentives", type: "TEXT", nullable: false, defaultValue: "1970-01-01 00:00:00");
            migrationBuilder.AddColumn<string>(name: "Epoch_Granularity", table: "Attentives", type: "TEXT", nullable: false, defaultValue: "Day");
            migrationBuilder.AddColumn<string>(name: "Epoch_Duration", table: "Attentives", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Epoch_TimeZone", table: "Attentives", type: "TEXT", nullable: true);
            migrationBuilder.Sql(@"
                UPDATE ""Attentives"" SET
                    ""Epoch_Moment"" = ""Date"" || ' ' || COALESCE(""Time"", '00:00:00'),
                    ""Epoch_Granularity"" = CASE WHEN ""Time"" IS NOT NULL THEN 'Minute' ELSE 'Day' END;");
            migrationBuilder.DropColumn(name: "Date", table: "Attentives");
            migrationBuilder.DropColumn(name: "Time", table: "Attentives");

            // --- Eventives ---
            migrationBuilder.AddColumn<string>(name: "Epoch_Moment", table: "Eventives", type: "TEXT", nullable: false, defaultValue: "1970-01-01 00:00:00");
            migrationBuilder.AddColumn<string>(name: "Epoch_Granularity", table: "Eventives", type: "TEXT", nullable: false, defaultValue: "Day");
            migrationBuilder.AddColumn<string>(name: "Epoch_Duration", table: "Eventives", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Epoch_TimeZone", table: "Eventives", type: "TEXT", nullable: true);
            migrationBuilder.Sql(@"
                UPDATE ""Eventives"" SET
                    ""Epoch_Moment"" = ""Date"" || ' ' || COALESCE(""StartTime"", '00:00:00'),
                    ""Epoch_Granularity"" = CASE WHEN ""StartTime"" IS NOT NULL THEN 'Minute' ELSE 'Day' END,
                    ""Epoch_Duration"" = CASE
                        WHEN ""StartTime"" IS NOT NULL AND ""EndTime"" IS NOT NULL
                        THEN CAST(CAST((strftime('%s', '2000-01-01 ' || ""EndTime"") - strftime('%s', '2000-01-01 ' || ""StartTime"")) / 60 AS INTEGER) AS TEXT) || 'm'
                        ELSE NULL END;");
            migrationBuilder.DropColumn(name: "Date", table: "Eventives");
            migrationBuilder.DropColumn(name: "StartTime", table: "Eventives");
            migrationBuilder.DropColumn(name: "EndTime", table: "Eventives");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort reverse: split the Moment back into Date + time-of-day. An eventive's EndTime cannot be
            // reconstructed from a nominal Duration, so it comes back null.
            migrationBuilder.AddColumn<DateOnly>(name: "Date", table: "Attentives", type: "TEXT", nullable: false, defaultValue: new DateOnly(1970, 1, 1));
            migrationBuilder.AddColumn<TimeOnly>(name: "Time", table: "Attentives", type: "TEXT", nullable: true);
            migrationBuilder.Sql(@"
                UPDATE ""Attentives"" SET
                    ""Date"" = date(""Epoch_Moment""),
                    ""Time"" = CASE WHEN ""Epoch_Granularity"" IN ('Hour', 'Minute', 'Second') THEN time(""Epoch_Moment"") ELSE NULL END;");
            migrationBuilder.DropColumn(name: "Epoch_Moment", table: "Attentives");
            migrationBuilder.DropColumn(name: "Epoch_Granularity", table: "Attentives");
            migrationBuilder.DropColumn(name: "Epoch_Duration", table: "Attentives");
            migrationBuilder.DropColumn(name: "Epoch_TimeZone", table: "Attentives");

            migrationBuilder.AddColumn<DateOnly>(name: "Date", table: "Eventives", type: "TEXT", nullable: false, defaultValue: new DateOnly(1970, 1, 1));
            migrationBuilder.AddColumn<TimeOnly>(name: "StartTime", table: "Eventives", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<TimeOnly>(name: "EndTime", table: "Eventives", type: "TEXT", nullable: true);
            migrationBuilder.Sql(@"
                UPDATE ""Eventives"" SET
                    ""Date"" = date(""Epoch_Moment""),
                    ""StartTime"" = CASE WHEN ""Epoch_Granularity"" IN ('Hour', 'Minute', 'Second') THEN time(""Epoch_Moment"") ELSE NULL END;");
            migrationBuilder.DropColumn(name: "Epoch_Moment", table: "Eventives");
            migrationBuilder.DropColumn(name: "Epoch_Granularity", table: "Eventives");
            migrationBuilder.DropColumn(name: "Epoch_Duration", table: "Eventives");
            migrationBuilder.DropColumn(name: "Epoch_TimeZone", table: "Eventives");
        }
    }
}
