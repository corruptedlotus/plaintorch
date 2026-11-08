using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <summary>
    /// Retires incentive nesting (PEP102 patch 9): an incentive no longer names a parent incentive.
    /// </summary>
    /// <remarks>
    /// The <c>ParentIncentiveId</c> column is emptied and its index dropped, but the column itself stays, unmapped.
    /// Dropping it, or its self-referencing foreign key, makes SQLite rebuild <c>Incentives</c> from this migration's
    /// model. This migration was written beside <c>DeclarativeTimeZone</c>, which adds an <c>Incentives</c> column and
    /// sorts after it, so a database that ran <c>DeclarativeTimeZone</c> first applies this one out of order, and a
    /// rebuild would drop that column and its values with it. An empty column satisfies its foreign key trivially, and
    /// the next rebuild of the table drops it. A database that ran this migration before the change has no such column,
    /// so it cannot step down past it.
    /// </remarks>
    public partial class RemoveIncentiveNesting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Incentives\" SET \"ParentIncentiveId\" = NULL;");

            migrationBuilder.DropIndex(
                name: "IX_Incentives_ParentIncentiveId",
                table: "Incentives");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Incentives_ParentIncentiveId",
                table: "Incentives",
                column: "ParentIncentiveId");
        }
    }
}
