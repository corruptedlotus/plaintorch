using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class BoundAttentivesToExecutives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A Polaris-bound attentive is now a decree-backed executive (PEP111). Carry each one across to an
            // Executive (its decree becomes the incentive, its done-state the executed flag, its allocation and
            // affinity preserved), then drop the bound attentives before the column goes. Grouping by (cycle, decree)
            // collapses any duplicates so the per-cycle uniqueness holds; the reward ledger for past executions
            // stays as it was — nothing is re-granted or revoked.
            migrationBuilder.Sql(@"
                INSERT INTO ""Executive"" (""PolarisCycleId"", ""IncentiveId"", ""Executed"", ""Estimation"", ""Minimum"", ""Maximum"", ""Elapsed"", ""AffinityTimeframeId"")
                SELECT ""PolarisCycleId"", ""DecreeId"",
                       MAX(CASE WHEN ""Resolution"" = 'Done' THEN 1 ELSE 0 END),
                       MAX(""Estimation""), MAX(""Minimum""), MAX(""Maximum""), 0, MAX(""AffinityTimeframeId"")
                FROM ""Attentives""
                WHERE ""PolarisCycleId"" IS NOT NULL
                GROUP BY ""PolarisCycleId"", ""DecreeId"";");
            migrationBuilder.Sql(@"DELETE FROM ""Attentives"" WHERE ""PolarisCycleId"" IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_Attentives_PolarisCycles_PolarisCycleId",
                table: "Attentives");

            migrationBuilder.DropIndex(
                name: "IX_Attentives_PolarisCycleId",
                table: "Attentives");

            migrationBuilder.DropColumn(
                name: "PolarisCycleId",
                table: "Attentives");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PolarisCycleId",
                table: "Attentives",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attentives_PolarisCycleId",
                table: "Attentives",
                column: "PolarisCycleId");

            migrationBuilder.AddForeignKey(
                name: "FK_Attentives_PolarisCycles_PolarisCycleId",
                table: "Attentives",
                column: "PolarisCycleId",
                principalTable: "PolarisCycles",
                principalColumn: "Id");
        }
    }
}
