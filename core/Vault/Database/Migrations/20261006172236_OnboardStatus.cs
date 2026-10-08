using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <summary>
    /// Renames the objective status <c>Onrush</c> to <c>Onboard</c> (PEP103, Patch103.3). Objective statuses are stored by
    /// name, so the rows that read <c>Onrush</c> are rewritten; the status keeps its place and its Open state. The notes are
    /// renamed by the v7 vault migration (<c>ObjectiveOnboardStatusMigration</c>). Graveyard payloads and audit details are
    /// left as written: the payloads store the status by number, and the audit is a record of what was.
    /// </summary>
    public partial class OnboardStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Incentives\" SET \"Status\" = 'Onboard' WHERE \"Discriminator\" = 'Objective' AND \"Status\" = 'Onrush';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Incentives\" SET \"Status\" = 'Onrush' WHERE \"Discriminator\" = 'Objective' AND \"Status\" = 'Onboard';");
        }
    }
}
