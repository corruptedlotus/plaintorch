using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <summary>
    /// Renames the fate status <c>Cancelled</c> to <c>Stopped</c> (PEP103, Patch103.6). Fate statuses are stored by name, so
    /// the rows that read <c>Cancelled</c> are rewritten; the status keeps its place and its Deferred state. The notes are
    /// renamed by the v8 vault migration (<c>FateStoppedStatusMigration</c>). Graveyard payloads and audit details are left
    /// as written: the payloads store the status by number, and the audit is a record of what was. An eventive's
    /// <c>Cancelled</c> resolution is another enum and stays as it is.
    /// </summary>
    public partial class FateStoppedStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Incentives\" SET \"FateStatus\" = 'Stopped' WHERE \"Discriminator\" = 'Fate' AND \"FateStatus\" = 'Cancelled';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Incentives\" SET \"FateStatus\" = 'Cancelled' WHERE \"Discriminator\" = 'Fate' AND \"FateStatus\" = 'Stopped';");
        }
    }
}
