using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Exercises the database migrations of PEP103 Patch103.6: <c>FateStoppedStatus</c> renames a fate stored as
/// <c>Cancelled</c> to <c>Stopped</c> and leaves every other row alone, and <c>BacklogRecordHistory</c> lets an owner hold
/// several records of one state, keeping only the latest of each slot when it goes back down.
/// </summary>
public sealed class StoppedStatusMigrationTests : IDisposable
{
	private const string BeforeHistory = "20261006172236_OnboardStatus";
	private const string BeforeStopped = "20261011152903_BacklogRecordHistory";

	private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"p7t-migration-{Guid.NewGuid():N}.db");

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task A_cancelled_fate_becomes_stopped_and_down_writes_the_old_name_back()
	{
		await MigrateAsync(BeforeStopped);
		await ExecuteAsync("""
			INSERT INTO Incentives (Id, Title, Discriminator, FateStatus) VALUES
				('f00000001', 'Stopped fate', 'Fate', 'Cancelled'),
				('f00000002', 'Running fate', 'Fate', 'Active');
			INSERT INTO Incentives (Id, Title, Discriminator, Status, Objective_College, CelestronValue) VALUES
				('QUEST01', 'Work', 'Objective', 'Standby', 'Unspecified', 0);
			""");

		await MigrateAsync();
		Assert.Equal("Stopped", await ScalarAsync("SELECT FateStatus FROM Incentives WHERE Id = 'f00000001'"));
		Assert.Equal("Active", await ScalarAsync("SELECT FateStatus FROM Incentives WHERE Id = 'f00000002'"));
		Assert.Equal("Standby", await ScalarAsync("SELECT Status FROM Incentives WHERE Id = 'QUEST01'"));
		await using (var context = CreateContext())
		{
			Assert.Equal(Pleiades.Orchestration.FateStatus.Stopped, (await context.Fates.AsNoTracking().IgnoreAutoIncludes().SingleAsync(fate => fate.Id == "f00000001", Ct)).Status);
		}

		await MigrateAsync(BeforeStopped);
		Assert.Equal("Cancelled", await ScalarAsync("SELECT FateStatus FROM Incentives WHERE Id = 'f00000001'"));
	}

	[Fact]
	public async Task Records_repeat_and_going_down_keeps_the_latest_of_each_slot()
	{
		await MigrateAsync();
		await ExecuteAsync("""
			INSERT INTO Incentives (Id, Title, Discriminator, FateStatus) VALUES ('f00000001', 'Fate', 'Fate', 'Active');
			INSERT INTO BacklogSchedules (Id, Type, State, Time, IncentiveId) VALUES
				(1, 'Record', 'Started', '2026-10-01 09:00:00', 'f00000001'),
				(2, 'Record', 'Deferred', '2026-10-02 09:00:00', 'f00000001'),
				(3, 'Record', 'Started', '2026-10-03 09:00:00', 'f00000001'),
				(4, 'Plan', 'Started', '2026-10-04 09:00:00', 'f00000001');
			""");

		// Plans stay unique to their owner and state.
		await Assert.ThrowsAnyAsync<DbException>(() => ExecuteAsync("INSERT INTO BacklogSchedules (Id, Type, State, Time, IncentiveId) VALUES (5, 'Plan', 'Started', '2026-10-05 09:00:00', 'f00000001');"));

		await MigrateAsync(BeforeHistory);
		Assert.Equal("2,3,4", await ScalarAsync("SELECT group_concat(Id, ',') FROM (SELECT Id FROM BacklogSchedules ORDER BY Id)"));
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

	private async Task ExecuteAsync(string sql)
	{
		await using var context = CreateContext();
		await context.Database.ExecuteSqlRawAsync(sql, Ct);
	}

	private async Task<string?> ScalarAsync(string sql)
	{
		await using var context = CreateContext();
		var connection = context.Database.GetDbConnection();
		await connection.OpenAsync(Ct);
		await using var command = connection.CreateCommand();
		command.CommandText = sql;
		return Convert.ToString(await command.ExecuteScalarAsync(Ct), CultureInfo.InvariantCulture);
	}
}
