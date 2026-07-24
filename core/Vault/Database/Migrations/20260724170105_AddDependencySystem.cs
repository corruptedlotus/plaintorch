using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddDependencySystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            // PEP101: seed the stable RECURRENCE-ID slot for pre-existing eventives from their current time
            // specification (best-effort — they predate any move, so current == original).
            migrationBuilder.Sql("UPDATE \"Eventives\" SET \"RecurrenceDate\" = \"Date\", \"RecurrenceTime\" = \"StartTime\";");

            migrationBuilder.CreateTable(
                name: "Checkpoints",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    CelestronToll = table.Column<int>(type: "INTEGER", nullable: true),
                    TollPaid = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExternalCondition = table.Column<bool>(type: "INTEGER", nullable: true),
                    Unlocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Checkpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Dependencies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceKind = table.Column<string>(type: "TEXT", nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", nullable: false),
                    SourceRecurrenceDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    SourceRecurrenceTime = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                    TargetKind = table.Column<string>(type: "TEXT", nullable: false),
                    TargetId = table.Column<string>(type: "TEXT", nullable: false),
                    TargetRecurrenceDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    TargetRecurrenceTime = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                    Trigger = table.Column<string>(type: "TEXT", nullable: true),
                    Constraint = table.Column<string>(type: "TEXT", nullable: true),
                    Satisfied = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dependencies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Dependencies_SourceId",
                table: "Dependencies",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_Dependencies_TargetId",
                table: "Dependencies",
                column: "TargetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Checkpoints");

            migrationBuilder.DropTable(
                name: "Dependencies");

            migrationBuilder.DropColumn(
                name: "RecurrenceDate",
                table: "Eventives");

            migrationBuilder.DropColumn(
                name: "RecurrenceTime",
                table: "Eventives");
        }
    }
}
