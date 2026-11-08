using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace plaintorch.Vault.Database.Migrations
{
    /// <summary>
    /// Moves backlog schedules onto the four lifecycle states (PEP103). Their state is stored by name: <c>Begin</c> becomes
    /// <c>Started</c> and <c>Finish</c> becomes <c>Finished</c>.
    /// </summary>
    /// <remarks>
    /// A fate no longer finishes: cancelling it sets it aside. Its finish records stand for when it was cancelled, so they
    /// become deferred records, and its finish plans have nothing left to place, so they go. Nothing else is backfilled:
    /// failing an item used to withdraw its start and finish records, which cannot be restored, and items that were already
    /// set aside (a failed objective, an abandoned decree, a lunar directive on hold) get their deferred record when they
    /// are next set aside, since when they were cannot be known. Decrees and lunar directives, which had no lifecycle,
    /// likewise start recording from their next change.
    /// </remarks>
    public partial class LifecycleStates : Migration
    {
        private const string Fates = "SELECT \"Id\" FROM \"Incentives\" WHERE \"Discriminator\" = 'Fate'";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"BacklogSchedules\" SET \"State\" = 'Started' WHERE \"State\" = 'Begin';");
            migrationBuilder.Sql("UPDATE \"BacklogSchedules\" SET \"State\" = 'Finished' WHERE \"State\" = 'Finish';");
            migrationBuilder.Sql($"UPDATE \"BacklogSchedules\" SET \"State\" = 'Deferred' WHERE \"State\" = 'Finished' AND \"Type\" = 'Record' AND \"IncentiveId\" IN ({Fates});");
            migrationBuilder.Sql($"DELETE FROM \"BacklogSchedules\" WHERE \"State\" = 'Finished' AND \"Type\" = 'Plan' AND \"IncentiveId\" IN ({Fates});");
        }

        /// <inheritdoc />
        /// <remarks>
        /// The two phases before knew no deferral. A cancelled fate's deferred record goes back to the finish record a
        /// cancellation was; every other deferred record is dropped.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"UPDATE \"BacklogSchedules\" SET \"State\" = 'Finished' WHERE \"State\" = 'Deferred' AND \"Type\" = 'Record' AND \"IncentiveId\" IN ({Fates} AND \"FateStatus\" = 'Cancelled');");
            migrationBuilder.Sql("DELETE FROM \"BacklogSchedules\" WHERE \"State\" = 'Deferred';");
            migrationBuilder.Sql("UPDATE \"BacklogSchedules\" SET \"State\" = 'Begin' WHERE \"State\" = 'Started';");
            migrationBuilder.Sql("UPDATE \"BacklogSchedules\" SET \"State\" = 'Finish' WHERE \"State\" = 'Finished';");
        }
    }
}
