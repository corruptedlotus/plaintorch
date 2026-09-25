using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Exercises the <c>OrbitScheduleStateHierarchy</c> migration (PEP100 patch 2) against schedule states written in the
/// pre-hierarchy shape — keyed by their incentive id alone — which a fresh-schema test vault never holds. Every existing
/// row becomes an <c>IncentiveOrbitScheduleState</c> with a fresh id and its incentive, state and timestamp intact;
/// <c>Down</c> folds the incentive states back and drops the timeframe states that have no place in the old table.
/// </summary>
public sealed class OrbitScheduleStateHierarchyMigrationTests : IDisposable
{
	private const string PreviousMigration = "20260923220349_OccurrenceRecurrenceId";
	private const string ThisMigration = "20260925001601_OrbitScheduleStateHierarchy";

	private const string DecreeState = """{"notation":"d","seed":7}""";
	private const string FateState = """{"notation":"w[d{1}]","seed":11}""";
	private const string DecreeUpdated = "2026-07-20 08:15:00+00:00";
	private const string FateUpdated = "2026-07-21 09:30:00+00:00";

	private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"p7t-migration-{Guid.NewGuid():N}.db");

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Existing_states_become_incentive_states_with_fresh_ids_and_intact_data()
	{
		await SeedPreMigrationRowsAsync();
		await MigrateAsync(ThisMigration);

		await using var context = CreateContext();
		var states = await context.IncentiveOrbitScheduleStates
			.AsNoTracking()
			.OrderBy(state => state.IncentiveId)
			.ToListAsync(Ct);

		Assert.Collection(
			states,
			decree =>
			{
				Assert.Equal("c-decree", decree.IncentiveId);
				Assert.Equal(DecreeState, decree.StateJson);
				Assert.Equal(DateTimeOffset.Parse(DecreeUpdated, CultureInfo.InvariantCulture), decree.UpdatedUtc);
			},
			fate =>
			{
				Assert.Equal("f-fate", fate.IncentiveId);
				Assert.Equal(FateState, fate.StateJson);
				Assert.Equal(DateTimeOffset.Parse(FateUpdated, CultureInfo.InvariantCulture), fate.UpdatedUtc);
			});

		// The rows had no id before; the rebuild hands out fresh, distinct autoincrement ids.
		Assert.All(states, state => Assert.True(state.Id > 0));
		Assert.Equal(2, states.Select(state => state.Id).Distinct().Count());

		// Nothing reads as a timeframe state, and the stored discriminator is the incentive kind itself.
		Assert.Empty(await context.TimeframeOrbitScheduleStates.AsNoTracking().ToListAsync(Ct));
		var connection = context.Database.GetDbConnection();
		await connection.OpenAsync(Ct);
		Assert.Equal("IncentiveOrbitScheduleState", await ScalarAsync(connection, "SELECT group_concat(DISTINCT Discriminator) FROM OrbitScheduleStates"));
		Assert.Equal("", await ScalarAsync(connection, "SELECT group_concat(\"table\") FROM pragma_foreign_key_check"));

		// The backfill default served the existing rows only: the rebuilt column keeps no default, so a later insert
		// without a kind fails loudly instead of being silently labelled an incentive state. The insert targets an
		// incentive that has no state yet, so the unique incentive index cannot be what refuses it.
		Assert.Equal("1", await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('OrbitScheduleStates') WHERE name = 'Discriminator' AND dflt_value IS NULL"));
		await using var spare = connection.CreateCommand();
		spare.CommandText = "INSERT INTO Incentives (Id, Discriminator, Title, DecreeStatus, College, ActiveCelestron, Reflect, Orbit) VALUES ('c-spare', 'Decree', 'Spare', 'Active', 0, 0, 0, 'd')";
		await spare.ExecuteNonQueryAsync(Ct);

		await using var kindless = connection.CreateCommand();
		kindless.CommandText = "INSERT INTO OrbitScheduleStates (IncentiveId, StateJson, UpdatedUtc) VALUES ('c-spare', '{}', '2026-07-22 00:00:00+00:00')";
		var refused = await Assert.ThrowsAnyAsync<DbException>(() => kindless.ExecuteNonQueryAsync(Ct));
		Assert.Contains("NOT NULL constraint failed: OrbitScheduleStates.Discriminator", refused.Message, StringComparison.Ordinal);

		// The same row with its kind named is accepted, so nothing but the missing discriminator refused it.
		await using var kinded = connection.CreateCommand();
		kinded.CommandText = "INSERT INTO OrbitScheduleStates (Discriminator, IncentiveId, StateJson, UpdatedUtc) VALUES ('IncentiveOrbitScheduleState', 'c-spare', '{}', '2026-07-22 00:00:00+00:00')";
		Assert.Equal(1, await kinded.ExecuteNonQueryAsync(Ct));
	}

	[Fact]
	public async Task A_migrated_incentive_state_still_cascades_with_its_incentive()
	{
		await SeedPreMigrationRowsAsync();
		await MigrateAsync(ThisMigration);

		await using var context = CreateContext();
		await context.Database.ExecuteSqlRawAsync("DELETE FROM Incentives WHERE Id = 'c-decree'", Ct);

		var remaining = await context.IncentiveOrbitScheduleStates.AsNoTracking().Select(state => state.IncentiveId).ToListAsync(Ct);
		Assert.Equal(["f-fate"], remaining);
	}

	[Fact]
	public async Task Down_keeps_the_incentive_states_and_drops_the_timeframe_states()
	{
		await SeedPreMigrationRowsAsync();
		await MigrateAsync(ThisMigration);

		await ExecuteAsync("""
			INSERT INTO Directives (Id, Discriminator, Title, Tags, LunarStatus) VALUES ('M1', 'LunarDirective', 'Moon', '[]', 'Active');
			INSERT INTO Timeframes (Id, DirectiveId, Title, StartTime, EndTime, AutoInclusion, AutoInclusionColleges) VALUES (5, 'M1', 'Dawn', '06:00:00', '08:00:00', 0, '[]');
			INSERT INTO OrbitScheduleStates (Discriminator, TimeframeId, StateJson, UpdatedUtc) VALUES ('TimeframeOrbitScheduleState', 5, '{}', '2026-07-22 00:00:00+00:00');
			""");

		await MigrateAsync(PreviousMigration);

		await using var downContext = CreateContext();
		var connection = downContext.Database.GetDbConnection();
		await connection.OpenAsync(Ct);
		Assert.Equal(
			$"c-decree|{DecreeState}|{DecreeUpdated};f-fate|{FateState}|{FateUpdated}",
			await ScalarAsync(connection, "SELECT group_concat(IncentiveId || '|' || StateJson || '|' || UpdatedUtc, ';') FROM (SELECT * FROM OrbitScheduleStates ORDER BY IncentiveId)"));
		Assert.Equal("IncentiveId,StateJson,UpdatedUtc", await ScalarAsync(connection, "SELECT group_concat(name) FROM (SELECT name FROM pragma_table_info('OrbitScheduleStates') ORDER BY name)"));
	}

	public void Dispose()
	{
		if (File.Exists(databasePath))
		{
			File.Delete(databasePath);
		}
	}

	// Foreign keys are on so the rebuild is judged against a coherent graph (states reference real incentives); pooling
	// is off so the database file can be deleted afterwards.
	private PlainfraContext CreateContext() => new(new DbContextOptionsBuilder<PlainfraContext>()
		.UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
		.Options);

	private async Task MigrateAsync(string targetMigration)
	{
		await using var context = CreateContext();
		await context.GetService<IMigrator>().MigrateAsync(targetMigration, Ct);
	}

	/// <summary>
	/// Builds the schema as it stood before the hierarchy and writes two declaratives with their incentive-keyed
	/// schedule states in that shape.
	/// </summary>
	private async Task SeedPreMigrationRowsAsync()
	{
		await MigrateAsync(PreviousMigration);

		await ExecuteAsync($$"""
			INSERT INTO Incentives (Id, Discriminator, Title, DecreeStatus, College, ActiveCelestron, Reflect, Orbit) VALUES
				('c-decree', 'Decree', 'Stretch', 'Active', 0, 0, 0, 'd');
			INSERT INTO Incentives (Id, Discriminator, Title, FateStatus, Orbit) VALUES
				('f-fate', 'Fate', 'Market', 'Active', 'w[d{1}]');
			INSERT INTO OrbitScheduleStates (IncentiveId, StateJson, UpdatedUtc) VALUES
				('c-decree', '{{DecreeState}}', '{{DecreeUpdated}}'),
				('f-fate', '{{FateState}}', '{{FateUpdated}}');
			""");
	}

	/// <summary>
	/// Runs raw SQL on a plain command — not through EF's raw-SQL API, which would read the JSON braces as format
	/// placeholders.
	/// </summary>
	private async Task ExecuteAsync(string sql)
	{
		await using var context = CreateContext();
		var connection = context.Database.GetDbConnection();
		await connection.OpenAsync(Ct);
		await using var command = connection.CreateCommand();
		command.CommandText = sql;
		await command.ExecuteNonQueryAsync(Ct);
	}

	private static async Task<string?> ScalarAsync(DbConnection connection, string sql)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = sql;
		return Convert.ToString(await command.ExecuteScalarAsync(Ct), CultureInfo.InvariantCulture);
	}
}
