using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class DependencyUniquenessAndOnrushMilestones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GraphLayout",
                table: "OnrushSprints",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MilestoneCheckpointId",
                table: "OnrushSprints",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnrushSprintId",
                table: "Checkpoints",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OnrushSprints_MilestoneCheckpointId",
                table: "OnrushSprints",
                column: "MilestoneCheckpointId");

            migrationBuilder.CreateIndex(
                name: "IX_Dependencies_SourceKind_SourceId_SourceRecurrenceDate_SourceRecurrenceTime_TargetKind_TargetId_TargetRecurrenceDate_TargetRecurrenceTime_Trigger_Constraint",
                table: "Dependencies",
                columns: new[] { "SourceKind", "SourceId", "SourceRecurrenceDate", "SourceRecurrenceTime", "TargetKind", "TargetId", "TargetRecurrenceDate", "TargetRecurrenceTime", "Trigger", "Constraint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Checkpoints_OnrushSprintId",
                table: "Checkpoints",
                column: "OnrushSprintId");

            migrationBuilder.AddForeignKey(
                name: "FK_Checkpoints_OnrushSprints_OnrushSprintId",
                table: "Checkpoints",
                column: "OnrushSprintId",
                principalTable: "OnrushSprints",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_OnrushSprints_Checkpoints_MilestoneCheckpointId",
                table: "OnrushSprints",
                column: "MilestoneCheckpointId",
                principalTable: "Checkpoints",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Checkpoints_OnrushSprints_OnrushSprintId",
                table: "Checkpoints");

            migrationBuilder.DropForeignKey(
                name: "FK_OnrushSprints_Checkpoints_MilestoneCheckpointId",
                table: "OnrushSprints");

            migrationBuilder.DropIndex(
                name: "IX_OnrushSprints_MilestoneCheckpointId",
                table: "OnrushSprints");

            migrationBuilder.DropIndex(
                name: "IX_Dependencies_SourceKind_SourceId_SourceRecurrenceDate_SourceRecurrenceTime_TargetKind_TargetId_TargetRecurrenceDate_TargetRecurrenceTime_Trigger_Constraint",
                table: "Dependencies");

            migrationBuilder.DropIndex(
                name: "IX_Checkpoints_OnrushSprintId",
                table: "Checkpoints");

            migrationBuilder.DropColumn(
                name: "GraphLayout",
                table: "OnrushSprints");

            migrationBuilder.DropColumn(
                name: "MilestoneCheckpointId",
                table: "OnrushSprints");

            migrationBuilder.DropColumn(
                name: "OnrushSprintId",
                table: "Checkpoints");
        }
    }
}
