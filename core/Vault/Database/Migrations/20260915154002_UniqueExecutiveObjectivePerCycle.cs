using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <inheritdoc />
    public partial class UniqueExecutiveObjectivePerCycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Executive_PolarisCycleId",
                table: "Executive");

            // A cycle may hold at most one executive per objective from here on. Vaults written before this rule
            // could hold repeats (every "add to Polaris" surface used to plan another one); keep the earliest of
            // each group so the unique index below can be built.
            migrationBuilder.Sql("""
                DELETE FROM "Executive"
                WHERE "ObjectiveId" IS NOT NULL
                  AND "Id" NOT IN (
                    SELECT MIN("Id") FROM "Executive"
                    WHERE "ObjectiveId" IS NOT NULL
                    GROUP BY "PolarisCycleId", "ObjectiveId");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Executive_PolarisCycleId_ObjectiveId",
                table: "Executive",
                columns: new[] { "PolarisCycleId", "ObjectiveId" },
                unique: true,
                filter: "\"ObjectiveId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Executive_PolarisCycleId_ObjectiveId",
                table: "Executive");

            migrationBuilder.CreateIndex(
                name: "IX_Executive_PolarisCycleId",
                table: "Executive",
                column: "PolarisCycleId");
        }
    }
}
