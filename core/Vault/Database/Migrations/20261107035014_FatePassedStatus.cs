using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <summary>
    /// Fates pass (PEP103 I-191): the new <c>Passed</c> status is stored by name like the others and needs no change, but how a
    /// fate passed does. <c>FatePassInferred</c> says whether the engine inferred its Passed rather than the user selecting it,
    /// and <c>FatePassedFrom</c> the started status it left. The flag is a column of the shared incentive table, so it is
    /// nullable there; every stored fate gets <c>0</c>, as no fate has passed yet. The fates whose orbits already ran out pass
    /// on the first rolling pass after the upgrade.
    /// </summary>
    public partial class FatePassedStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FatePassInferred",
                table: "Incentives",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FatePassedFrom",
                table: "Incentives",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql("UPDATE \"Incentives\" SET \"FatePassInferred\" = 0 WHERE \"Discriminator\" = 'Fate';");
        }

        /// <summary>
        /// Takes every pass back to a status the core before it reads: an inferred pass returns to the started status it left
        /// (Active when it left Stopped), and a selected one, which acted like Stopped, becomes Stopped. Its history records
        /// stay, as a record of what was.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Incentives\" SET \"FateStatus\" = COALESCE(\"FatePassedFrom\", 'Active') WHERE \"Discriminator\" = 'Fate' AND \"FateStatus\" = 'Passed' AND \"FatePassInferred\" = 1;");
            migrationBuilder.Sql("UPDATE \"Incentives\" SET \"FateStatus\" = 'Stopped' WHERE \"Discriminator\" = 'Fate' AND \"FateStatus\" = 'Passed';");

            migrationBuilder.DropColumn(
                name: "FatePassInferred",
                table: "Incentives");

            migrationBuilder.DropColumn(
                name: "FatePassedFrom",
                table: "Incentives");
        }
    }
}
