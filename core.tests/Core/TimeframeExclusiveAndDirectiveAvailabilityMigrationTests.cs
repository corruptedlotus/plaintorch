using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pleiades.Orchestration;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Exercises the <c>TimeframeExclusiveAndDirectiveAvailability</c> migration (PEP100 patch 2) against a populated vault
/// database. Adding the availability foreign key makes SQLite rebuild the heavily referenced <c>Directives</c> table;
/// the directive tree, the lunar directive's timeframes, the incentives and the executives affined to a timeframe must
/// all survive it, with timeframes defaulting to non-exclusive and directives to no availability. <c>Down</c> drops the
/// two columns and keeps every row.
/// </summary>
public sealed class TimeframeExclusiveAndDirectiveAvailabilityMigrationTests : IDisposable
{
	private const string PreviousMigration = "20260925001601_OrbitScheduleStateHierarchy";
	private const string ThisMigration = "20260925001846_TimeframeExclusiveAndDirectiveAvailability";

	private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"p7t-migration-{Guid.NewGuid():N}.db");

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task The_directive_rebuild_keeps_the_graph_and_defaults_the_new_columns()
	{
		await SeedPreMigrationRowsAsync();
		await MigrateAsync(ThisMigration);

		await using var context = CreateContext();

		var directives = await context.Directives.AsNoTracking().OrderBy(directive => directive.Id).ToListAsync(Ct);
		Assert.Equal(["A1", "A2", "M1"], directives.Select(directive => directive.Id));
		Assert.Equal("A1", directives.Single(directive => directive.Id == "A2").ParentDirectiveId);
		Assert.Equal("RAPTOR", directives.Single(directive => directive.Id == "A1").Codename);
		Assert.Equal(["focus"], directives.Single(directive => directive.Id == "A1").Tags);
		Assert.IsType<LunarDirective>(directives.Single(directive => directive.Id == "M1"));
		Assert.All(directives, directive => Assert.Null(directive.AvailabilityTimeframeId));

		var timeframes = await context.Timeframes.AsNoTracking().OrderBy(timeframe => timeframe.Id).ToListAsync(Ct);
		Assert.Equal([7L, 8L], timeframes.Select(timeframe => timeframe.Id));
		Assert.All(timeframes, timeframe => Assert.Equal("M1", timeframe.DirectiveId));
		Assert.All(timeframes, timeframe => Assert.False(timeframe.Exclusive));
		Assert.Equal(TimeframeInclusion.College, timeframes[1].AutoInclusion);
		Assert.Equal([ObjectiveCollege.Lore], timeframes[1].AutoInclusionColleges);

		var connection = context.Database.GetDbConnection();
		await connection.OpenAsync(Ct);
		Assert.Equal("j-objective|A2;k-decree|M1", await ScalarAsync(connection, "SELECT group_concat(Id || '|' || DirectiveId, ';') FROM (SELECT * FROM Incentives ORDER BY Id)"));
		Assert.Equal("j-objective|7;k-decree|8", await ScalarAsync(connection, "SELECT group_concat(IncentiveId || '|' || AffinityTimeframeId, ';') FROM (SELECT * FROM Executive ORDER BY IncentiveId)"));
		Assert.Equal("", await ScalarAsync(connection, "SELECT group_concat(\"table\") FROM pragma_foreign_key_check"));
	}

	[Fact]
	public async Task The_new_availability_key_clears_on_timeframe_delete_while_the_owner_cascade_still_holds()
	{
		await SeedPreMigrationRowsAsync();
		await MigrateAsync(ThisMigration);

		await using var context = CreateContext();
		await context.Database.ExecuteSqlRawAsync("UPDATE Directives SET AvailabilityTimeframeId = 7 WHERE Id = 'A2'", Ct);
		await context.Database.ExecuteSqlRawAsync("DELETE FROM Timeframes WHERE Id = 7", Ct);

		var connection = context.Database.GetDbConnection();
		await connection.OpenAsync(Ct);
		Assert.Equal("", await ScalarAsync(connection, "SELECT COALESCE(AvailabilityTimeframeId, '') FROM Directives WHERE Id = 'A2'"));
		// The executive affined to the deleted timeframe is cleared by its own (pre-existing) SetNull key.
		Assert.Equal("", await ScalarAsync(connection, "SELECT COALESCE(AffinityTimeframeId, '') FROM Executive WHERE IncentiveId = 'j-objective'"));

		// On the rebuilt table, deleting the owning lunar directive still cascades its remaining timeframe and that
		// timeframe's orbit state, while the availability pointing at it clears instead of blocking the delete.
		await ExecuteAsync("""
			INSERT INTO OrbitScheduleStates (Discriminator, TimeframeId, StateJson, UpdatedUtc) VALUES ('TimeframeOrbitScheduleState', 8, '{}', '2026-07-22 00:00:00+00:00');
			UPDATE Directives SET AvailabilityTimeframeId = 8 WHERE Id = 'A1';
			UPDATE Incentives SET DirectiveId = NULL WHERE Id = 'k-decree';
			DELETE FROM Directives WHERE Id = 'M1';
			""");
		Assert.Equal("0", await ScalarAsync(connection, "SELECT COUNT(*) FROM Timeframes"));
		Assert.Equal("0", await ScalarAsync(connection, "SELECT COUNT(*) FROM OrbitScheduleStates WHERE TimeframeId = 8"));
		Assert.Equal("", await ScalarAsync(connection, "SELECT COALESCE(AvailabilityTimeframeId, '') FROM Directives WHERE Id = 'A1'"));
		Assert.Equal("", await ScalarAsync(connection, "SELECT COALESCE(AffinityTimeframeId, '') FROM Executive WHERE IncentiveId = 'k-decree'"));
		Assert.Equal("A1,A2", await ScalarAsync(connection, "SELECT group_concat(Id) FROM (SELECT Id FROM Directives ORDER BY Id)"));
		Assert.Equal("", await ScalarAsync(connection, "SELECT group_concat(\"table\") FROM pragma_foreign_key_check"));
	}

	[Fact]
	public async Task Down_drops_the_columns_and_keeps_every_row()
	{
		await SeedPreMigrationRowsAsync();
		await MigrateAsync(ThisMigration);
		await MigrateAsync(PreviousMigration);

		await using var context = CreateContext();
		var connection = context.Database.GetDbConnection();
		await connection.OpenAsync(Ct);
		Assert.Equal("0", await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('Directives') WHERE name = 'AvailabilityTimeframeId'"));
		Assert.Equal("0", await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('Timeframes') WHERE name = 'Exclusive'"));
		Assert.Equal("A1,A2,M1", await ScalarAsync(connection, "SELECT group_concat(Id) FROM (SELECT Id FROM Directives ORDER BY Id)"));
		Assert.Equal("7,8", await ScalarAsync(connection, "SELECT group_concat(Id) FROM (SELECT Id FROM Timeframes ORDER BY Id)"));
		Assert.Equal("2", await ScalarAsync(connection, "SELECT COUNT(*) FROM Executive WHERE AffinityTimeframeId IS NOT NULL"));
	}

	public void Dispose()
	{
		if (File.Exists(databasePath))
		{
			File.Delete(databasePath);
		}
	}

	// Foreign keys are on and the seeded graph is coherent, so the rebuild is judged the way a real vault runs it;
	// pooling is off so the database file can be deleted afterwards.
	private PlainfraContext CreateContext() => new(new DbContextOptionsBuilder<PlainfraContext>()
		.UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
		.Options);

	private async Task MigrateAsync(string targetMigration)
	{
		await using var context = CreateContext();
		await context.GetService<IMigrator>().MigrateAsync(targetMigration, Ct);
	}

	/// <summary>
	/// Builds the schema as it stood before this migration and writes a small, coherent vault: a stellar directive with
	/// a subdirective, a lunar directive owning two timeframes (one college-included), an objective and a decree, and a
	/// running cycle whose executives are affined to those timeframes.
	/// </summary>
	private async Task SeedPreMigrationRowsAsync()
	{
		await MigrateAsync(PreviousMigration);

		await ExecuteAsync("""
			INSERT INTO Directives (Id, Discriminator, Title, Codename, Tags, Status, ParentDirectiveId) VALUES
				('A1', 'StellarDirective', 'Raptor', 'RAPTOR', '["focus"]', 'Active', NULL),
				('A2', 'StellarDirective', 'Wing', NULL, '[]', 'Active', 'A1');
			INSERT INTO Directives (Id, Discriminator, Title, Tags, LunarStatus) VALUES
				('M1', 'LunarDirective', 'Moon Law', '[]', 'Active');
			INSERT INTO Timeframes (Id, DirectiveId, Title, StartTime, EndTime, Orbit, AutoInclusion, AutoInclusionColleges) VALUES
				(7, 'M1', 'Dawn', '06:00:00', '08:00:00', NULL, 0, '[]'),
				(8, 'M1', 'Lab', '14:00:00', '17:00:00', 'w[d{1~5}]', 1, '[3]');
			INSERT INTO Incentives (Id, Discriminator, Title, DirectiveId, Status, Objective_College, CelestronValue) VALUES
				('j-objective', 'Objective', 'Draft', 'A2', 'Active', 'Lore', 5);
			INSERT INTO Incentives (Id, Discriminator, Title, DirectiveId, DecreeStatus, College, ActiveCelestron, Reflect, Orbit) VALUES
				('k-decree', 'Decree', 'Stretch', 'M1', 'Active', 0, 0, 0, 'd');
			INSERT INTO PolarisCycles (Id, Title, StartTime) VALUES ('P1', 'Today', '2026-07-20 08:00:00+00:00');
			INSERT INTO Executive (PolarisCycleId, IncentiveId, AffinityTimeframeId, Elapsed, Executed) VALUES
				('P1', 'j-objective', 7, 0, 0),
				('P1', 'k-decree', 8, 0, 0);
			""");
	}

	/// <summary>
	/// Runs raw SQL on a plain command — not through EF's raw-SQL API, which would read the orbit braces as format
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
