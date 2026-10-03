using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Exercises the <c>BacklogSchedules</c> migration's SQL against directives written in the pre-migration shape (PEP101)
/// — something a fresh-schema test vault never does. A stellar directive's start and end dates become its planned begin
/// and finish at midnight before the columns are dropped; <c>Down</c> writes the directive plans back as dates.
/// </summary>
/// <remarks>
/// Foreign keys stay on, as in a live vault, so the table rebuild that drops the columns is proven not to cascade the
/// freshly backfilled schedules away with the old directive table.
/// </remarks>
public sealed class BacklogSchedulesMigrationTests : IDisposable
{
	private const string PreviousMigration = "20260925001846_TimeframeExclusiveAndDirectiveAvailability";
	private const string ThisMigration = "20260930054809_BacklogSchedules";

	private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"p7t-migration-{Guid.NewGuid():N}.db");

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Start_and_end_dates_become_planned_begins_and_finishes_at_midnight()
	{
		await SeedPreMigrationRowsAsync();
		await MigrateAsync(ThisMigration);

		await using var context = CreateContext();

		// Equality lookups find the migrated rows: the backfilled text is exactly the form EF writes and binds.
		var begin = await context.BacklogSchedules.SingleAsync(item => item.DirectiveId == "A00000001" && item.Time == new DateTime(2026, 7, 1), Ct);
		Assert.Equal(BacklogScheduleType.Plan, begin.Type);
		Assert.Equal(LifecyclePhase.Begin, begin.State);
		var finish = await context.BacklogSchedules.SingleAsync(item => item.DirectiveId == "A00000001" && item.State == LifecyclePhase.Finish, Ct);
		Assert.Equal(BacklogScheduleType.Plan, finish.Type);
		Assert.Equal(new DateTime(2026, 9, 1), finish.Time);

		// Only the dates a directive had become plans; nothing is recorded, and no other owner gains a schedule.
		var endOnly = await context.BacklogSchedules.SingleAsync(item => item.DirectiveId == "A00000002", Ct);
		Assert.Equal(LifecyclePhase.Finish, endOnly.State);
		Assert.Equal(new DateTime(2026, 9, 15), endOnly.Time);
		Assert.Equal(3, await context.BacklogSchedules.CountAsync(Ct));
		Assert.False(await context.BacklogSchedules.AnyAsync(item => item.Type == BacklogScheduleType.Record, Ct));

		// The directives survive the table rebuild whole, and the dropped columns are gone. Read through SQL rather than
		// the entity: the database sits at this migration, and later migrations have since added directive columns the
		// current model would select.
		var connection = context.Database.GetDbConnection();
		await connection.OpenAsync(Ct);
		Assert.Equal("A00000001;A00000002;A00000003;LUNA001", await ScalarAsync(connection, "SELECT group_concat(Id, ';') FROM (SELECT Id FROM Directives ORDER BY Id)"));
		Assert.Equal("StellarDirective|Active", await ScalarAsync(connection, "SELECT Discriminator || '|' || Status FROM Directives WHERE Id = 'A00000001'"));
		Assert.Equal("0", await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('Directives') WHERE name IN ('StartDate', 'EndDate')"));
	}

	[Fact]
	public async Task Schedules_survive_the_later_table_rebuilds()
	{
		await SeedPreMigrationRowsAsync();
		await MigrateAsync(ThisMigration);

		await using (var context = CreateContext())
		{
			// An objective with a record of its own beside the directives' backfilled plans. Later migrations rebuild the
			// Directives table (adding the onrush foreign key) and empty the nesting column of Incentives; a rebuild that
			// dropped a table with foreign keys on would cascade its schedules away.
			await context.Database.ExecuteSqlRawAsync("""
				INSERT INTO Incentives (Id, Title, Discriminator, Status, Objective_College, CelestronValue) VALUES
					('QUEST01', 'Quest', 'Objective', 'Polaris', 'WorldQuest', 0);
				INSERT INTO BacklogSchedules (Type, State, Time, IncentiveId) VALUES
					('Record', 'Begin', '2026-09-30 10:00:00', 'QUEST01');
				""", Ct);
		}

		await MigrateAsync();

		await using var migrated = CreateContext();
		Assert.Empty(await migrated.Database.GetPendingMigrationsAsync(Ct));
		Assert.Equal(3, await migrated.BacklogSchedules.CountAsync(item => item.DirectiveId != null, Ct));
		var record = await migrated.BacklogSchedules.SingleAsync(item => item.IncentiveId == "QUEST01", Ct);
		Assert.Equal((BacklogScheduleType.Record, LifecyclePhase.Begin, new DateTime(2026, 9, 30, 10, 0, 0)), (record.Type, record.State, record.Time));

		// Every row came through, and the directives carry the later shape without the old dates.
		var connection = migrated.Database.GetDbConnection();
		await connection.OpenAsync(Ct);
		Assert.Equal("4|1", await ScalarAsync(connection, "SELECT (SELECT COUNT(*) FROM Directives) || '|' || (SELECT COUNT(*) FROM Incentives)"));
		Assert.Equal("OnrushSprintId", await ScalarAsync(connection, "SELECT group_concat(name) FROM pragma_table_info('Directives') WHERE name IN ('StartDate', 'EndDate', 'OnrushSprintId')"));
		Assert.Equal("", await ScalarAsync(connection, "SELECT group_concat(\"table\") FROM pragma_foreign_key_check"));
	}

	[Fact]
	public async Task Down_writes_the_directive_plans_back_as_dates()
	{
		await SeedPreMigrationRowsAsync();
		await MigrateAsync(ThisMigration);

		await using (var context = CreateContext())
		{
			// Written after the migration: a timed plan (whose time of day Down cannot keep), a record (not a date the old
			// shape held), and a lunar plan (lunar directives never had the columns).
			await context.Database.ExecuteSqlRawAsync("""
				INSERT INTO BacklogSchedules (Type, State, Time, DirectiveId) VALUES
					('Plan', 'Begin', '2026-10-05 09:30:00', 'A00000003'),
					('Record', 'Finish', '2026-10-06 18:00:00', 'A00000003'),
					('Plan', 'Begin', '2026-11-01 00:00:00', 'LUNA001');
				""", Ct);
		}

		await MigrateAsync(PreviousMigration);

		await using var reverted = CreateContext();
		var connection = reverted.Database.GetDbConnection();
		await connection.OpenAsync(Ct);
		Assert.Equal("2026-07-01|2026-09-01", await ScalarAsync(connection, "SELECT StartDate || '|' || EndDate FROM Directives WHERE Id = 'A00000001'"));
		Assert.Equal("|2026-09-15", await ScalarAsync(connection, "SELECT COALESCE(StartDate, '') || '|' || EndDate FROM Directives WHERE Id = 'A00000002'"));
		Assert.Equal("2026-10-05|", await ScalarAsync(connection, "SELECT StartDate || '|' || COALESCE(EndDate, '') FROM Directives WHERE Id = 'A00000003'"));
		Assert.Equal("|", await ScalarAsync(connection, "SELECT COALESCE(StartDate, '') || '|' || COALESCE(EndDate, '') FROM Directives WHERE Id = 'LUNA001'"));
		Assert.Equal("0", await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'BacklogSchedules'"));
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
	/// Builds the schema as it stood before this migration and writes directives in that shape: one with both dates, one
	/// with only an end date, one with neither, and a lunar directive.
	/// </summary>
	private async Task SeedPreMigrationRowsAsync()
	{
		await MigrateAsync(PreviousMigration);

		await using var context = CreateContext();
		await context.Database.ExecuteSqlRawAsync("""
			INSERT INTO Directives (Id, Title, Tags, Discriminator, Status, LunarStatus, StartDate, EndDate) VALUES
				('A00000001', 'Both dates', '[]', 'StellarDirective', 'Active', NULL, '2026-07-01', '2026-09-01'),
				('A00000002', 'End only', '[]', 'StellarDirective', 'Planned', NULL, NULL, '2026-09-15'),
				('A00000003', 'No dates', '[]', 'StellarDirective', 'Planned', NULL, NULL, NULL),
				('LUNA001', 'Moon law', '[]', 'LunarDirective', NULL, 'Active', NULL, NULL);
			""", Ct);
	}

	private static async Task<string?> ScalarAsync(DbConnection connection, string sql)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = sql;
		return Convert.ToString(await command.ExecuteScalarAsync(Ct), CultureInfo.InvariantCulture);
	}
}
