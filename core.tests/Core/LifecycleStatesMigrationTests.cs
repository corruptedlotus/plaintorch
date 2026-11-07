using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Exercises the <c>LifecycleStates</c> migration's SQL against schedules written in the two-phase shape (PEP103): begins
/// become starts and finishes finishes, a cancelled fate's finish record becomes the deferral it now is, and a fate's
/// finish plan, which nothing can place any more, goes. <c>Down</c> writes the old phases back.
/// </summary>
public sealed class LifecycleStatesMigrationTests : IDisposable
{
	private const string PreviousMigration = "20260930230346_DeclarativeTimeZone";

	private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"p7t-migration-{Guid.NewGuid():N}.db");

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Phases_become_states_and_a_cancelled_fate_is_deferred_rather_than_finished()
	{
		await SeedTwoPhaseSchedulesAsync();
		await MigrateAsync();

		await using var context = CreateContext();
		var connection = context.Database.GetDbConnection();
		await connection.OpenAsync(Ct);
		Assert.Equal("Record|Started;Plan|Finished", await SchedulesOfAsync(connection, "QUEST01"));
		Assert.Equal("Record|Started;Record|Deferred", await SchedulesOfAsync(connection, "f00000001"));
		Assert.Equal("Record|Started;Plan|Started", await SchedulesOfAsync(connection, "f00000002"));
	}

	[Fact]
	public async Task Down_writes_the_phases_back()
	{
		await SeedTwoPhaseSchedulesAsync();
		await MigrateAsync();

		await using (var context = CreateContext())
		{
			// Written after the migration: deferrals the old phases cannot hold, on an objective and an active fate.
			await context.Database.ExecuteSqlRawAsync("""
				INSERT INTO BacklogSchedules (Type, State, Time, IncentiveId) VALUES
					('Record', 'Deferred', '2026-10-05 12:00:00', 'QUEST01'),
					('Record', 'Deferred', '2026-10-05 12:00:00', 'f00000002');
				""", Ct);
		}

		await MigrateAsync(PreviousMigration);

		await using var reverted = CreateContext();
		var connection = reverted.Database.GetDbConnection();
		await connection.OpenAsync(Ct);
		Assert.Equal("Record|Begin;Plan|Finish", await SchedulesOfAsync(connection, "QUEST01"));
		// The cancellation reads as the finish it was; the finish plan dropped on the way up is not restored.
		Assert.Equal("Record|Begin;Record|Finish", await SchedulesOfAsync(connection, "f00000001"));
		Assert.Equal("Record|Begin;Plan|Begin", await SchedulesOfAsync(connection, "f00000002"));
	}

	public void Dispose()
	{
		if (File.Exists(databasePath))
		{
			File.Delete(databasePath);
		}
	}

	// Pooling is off so the database file can be deleted afterwards.
	private PlainfraContext CreateContext() => new(new DbContextOptionsBuilder<PlainfraContext>()
		.UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
		.Options);

	/// <summary>
	/// Migrates the database to <paramref name="targetMigration"/>, or to the latest migration when none is given.
	/// </summary>
	private async Task MigrateAsync(string? targetMigration = null)
	{
		await using var context = CreateContext();
		await context.GetService<IMigrator>().MigrateAsync(targetMigration, Ct);
	}

	/// <summary>
	/// Builds the schema as it stood before this migration and writes schedules in the two-phase shape: a begun objective
	/// with a finish plan, a cancelled fate that began and finished (by cancelling) and had a finish plan, and an active
	/// fate with a begin plan.
	/// </summary>
	private async Task SeedTwoPhaseSchedulesAsync()
	{
		await MigrateAsync(PreviousMigration);

		await using var context = CreateContext();
		await context.Database.ExecuteSqlRawAsync("""
			INSERT INTO Incentives (Id, Title, Discriminator, Status, Objective_College, CelestronValue) VALUES
				('QUEST01', 'Quest', 'Objective', 'Polaris', 'Unspecified', 0);
			INSERT INTO Incentives (Id, Title, Discriminator, FateStatus) VALUES
				('f00000001', 'Cancelled fate', 'Fate', 'Cancelled'),
				('f00000002', 'Active fate', 'Fate', 'Active');
			INSERT INTO BacklogSchedules (Type, State, Time, IncentiveId) VALUES
				('Record', 'Begin', '2026-09-01 09:00:00', 'QUEST01'),
				('Plan', 'Finish', '2026-11-01 00:00:00', 'QUEST01'),
				('Record', 'Begin', '2026-09-02 09:00:00', 'f00000001'),
				('Record', 'Finish', '2026-09-20 18:00:00', 'f00000001'),
				('Plan', 'Finish', '2026-12-01 00:00:00', 'f00000001'),
				('Record', 'Begin', '2026-09-03 09:00:00', 'f00000002'),
				('Plan', 'Begin', '2026-10-01 00:00:00', 'f00000002');
			""", Ct);
	}

	/// <summary>
	/// Lists an owner's schedules as <c>Type|State</c>, in the order they were written.
	/// </summary>
	private static async Task<string?> SchedulesOfAsync(DbConnection connection, string ownerId)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = $"SELECT group_concat(Type || '|' || State, ';') FROM (SELECT * FROM BacklogSchedules WHERE IncentiveId = '{ownerId}' ORDER BY Id)";
		return Convert.ToString(await command.ExecuteScalarAsync(Ct), CultureInfo.InvariantCulture);
	}
}
