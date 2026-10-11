using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <summary>
    /// Lets an owner hold several records of one state (PEP103, Patch103.6): a fate, a decree and a lunar directive keep a
    /// history of the states they enter. The owner indexes stop being unique, and only plans stay unique to their owner and
    /// state. Going down keeps the latest record of each owner, type and state, so the unique indexes can stand again.
    /// </summary>
    public partial class BacklogRecordHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BacklogSchedules_CheckpointId_Type_State",
                table: "BacklogSchedules");

            migrationBuilder.DropIndex(
                name: "IX_BacklogSchedules_DirectiveId_Type_State",
                table: "BacklogSchedules");

            migrationBuilder.DropIndex(
                name: "IX_BacklogSchedules_IncentiveId_Type_State",
                table: "BacklogSchedules");

            migrationBuilder.CreateIndex(
                name: "IX_BacklogSchedules_CheckpointId_Type_State",
                table: "BacklogSchedules",
                columns: new[] { "CheckpointId", "Type", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_BacklogSchedules_CheckpointPlan",
                table: "BacklogSchedules",
                columns: new[] { "CheckpointId", "State" },
                unique: true,
                filter: "\"Type\" = 'Plan'");

            migrationBuilder.CreateIndex(
                name: "IX_BacklogSchedules_DirectiveId_Type_State",
                table: "BacklogSchedules",
                columns: new[] { "DirectiveId", "Type", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_BacklogSchedules_DirectivePlan",
                table: "BacklogSchedules",
                columns: new[] { "DirectiveId", "State" },
                unique: true,
                filter: "\"Type\" = 'Plan'");

            migrationBuilder.CreateIndex(
                name: "IX_BacklogSchedules_IncentiveId_Type_State",
                table: "BacklogSchedules",
                columns: new[] { "IncentiveId", "Type", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_BacklogSchedules_IncentivePlan",
                table: "BacklogSchedules",
                columns: new[] { "IncentiveId", "State" },
                unique: true,
                filter: "\"Type\" = 'Plan'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BacklogSchedules_CheckpointId_Type_State",
                table: "BacklogSchedules");

            migrationBuilder.DropIndex(
                name: "IX_BacklogSchedules_CheckpointPlan",
                table: "BacklogSchedules");

            migrationBuilder.DropIndex(
                name: "IX_BacklogSchedules_DirectiveId_Type_State",
                table: "BacklogSchedules");

            migrationBuilder.DropIndex(
                name: "IX_BacklogSchedules_DirectivePlan",
                table: "BacklogSchedules");

            migrationBuilder.DropIndex(
                name: "IX_BacklogSchedules_IncentiveId_Type_State",
                table: "BacklogSchedules");

            migrationBuilder.DropIndex(
                name: "IX_BacklogSchedules_IncentivePlan",
                table: "BacklogSchedules");

            migrationBuilder.Sql("DELETE FROM \"BacklogSchedules\" WHERE \"Id\" NOT IN (SELECT MAX(\"Id\") FROM \"BacklogSchedules\" GROUP BY \"IncentiveId\", \"DirectiveId\", \"CheckpointId\", \"Type\", \"State\");");

            migrationBuilder.CreateIndex(
                name: "IX_BacklogSchedules_CheckpointId_Type_State",
                table: "BacklogSchedules",
                columns: new[] { "CheckpointId", "Type", "State" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BacklogSchedules_DirectiveId_Type_State",
                table: "BacklogSchedules",
                columns: new[] { "DirectiveId", "Type", "State" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BacklogSchedules_IncentiveId_Type_State",
                table: "BacklogSchedules",
                columns: new[] { "IncentiveId", "Type", "State" },
                unique: true);
        }
    }
}
