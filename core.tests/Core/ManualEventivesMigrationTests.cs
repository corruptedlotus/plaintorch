using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pleiades.Orbits;
using Pleiades.Orchestration;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Exercises the <c>ManualEventives</c> migration against eventives written before it, which a fresh-schema test
/// vault never holds. Relaxing the RECURRENCE-ID rebuilds the table, so every existing eventive must come through
/// with its slot and epoch intact; the column then admits a manual (slotless) eventive while the attentive column
/// stays required. <c>Down</c> keeps a manual eventive by pinning its current moment as its slot.
/// </summary>
public sealed class ManualEventivesMigrationTests : IDisposable
{
	private const string PreviousMigration = "20260925001846_TimeframeExclusiveAndDirectiveAvailability";
	private const string ThisMigration = "20260930141546_ManualEventives";

	private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"p7t-migration-{Guid.NewGuid():N}.db");

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Existing_eventives_keep_their_slot_and_a_manual_one_is_admitted()
	{
		await SeedPreMigrationRowsAsync();
		await MigrateAsync(ThisMigration);

		await using var context = CreateContext();
		var eventives = await context.Eventives
			.AsNoTracking()
			.IgnoreAutoIncludes()
			.OrderBy(item => item.Id)
			.ToListAsync(Ct);

		Assert.Collection(
			eventives,
			timed =>
			{
				Assert.Equal(1, timed.Id);
				Assert.Equal("f-fate", timed.FateId);
				Assert.Equal(new DateTime(2026, 7, 20, 14, 30, 0), timed.RecurrenceId);
				// Moved an hour later than its slot: the rebuild must not confuse the moment with the slot.
				Assert.Equal(new DateTime(2026, 7, 20, 15, 30, 0), timed.Epoch.Moment);
				Assert.Equal(OrbitUnit.Minute, timed.Epoch.Granularity);
				Assert.Equal("1h", timed.Epoch.Duration);
				Assert.Equal(EventiveResolution.Missed, timed.Resolution);
				Assert.False(timed.IsManual);
			},
			allDay =>
			{
				Assert.Equal(2, allDay.Id);
				Assert.Equal("o-objective", allDay.ObjectiveId);
				Assert.Equal(new DateTime(2026, 7, 21), allDay.RecurrenceId);
				Assert.False(allDay.IsManual);
			});

		// The rebuilt eventive column admits a slotless row; the attentive column still refuses one.
		await ExecuteAsync("""
			INSERT INTO Eventives (Id, FateId, Resolution, Epoch_Moment, Epoch_Granularity) VALUES
				(3, 'f-fate', 'Pending', '2026-07-22 18:00:00', 'Minute');
			""");
		var manual = await context.Eventives.AsNoTracking().IgnoreAutoIncludes().SingleAsync(item => item.Id == 3, Ct);
		Assert.Null(manual.RecurrenceId);
		Assert.True(manual.IsManual);

		var refused = await Assert.ThrowsAnyAsync<DbException>(() => ExecuteAsync("""
			INSERT INTO Attentives (DecreeId, Resolution, Epoch_Moment, Epoch_Granularity) VALUES
				('c-decree', 'Pending', '2026-07-22 00:00:00', 'Day');
			"""));
		Assert.Contains("NOT NULL constraint failed: Attentives.RecurrenceId", refused.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Down_keeps_a_manual_eventive_by_pinning_its_moment_as_its_slot()
	{
		await SeedPreMigrationRowsAsync();
		await MigrateAsync(ThisMigration);
		await ExecuteAsync("""
			INSERT INTO Eventives (Id, FateId, Resolution, Epoch_Moment, Epoch_Granularity, Epoch_Duration) VALUES
				(3, 'f-fate', 'Pending', '2026-07-22 18:00:00', 'Minute', '1h30m');
			""");

		await MigrateAsync(PreviousMigration);

		await using var context = CreateContext();
		var connection = context.Database.GetDbConnection();
		await connection.OpenAsync(Ct);
		Assert.Equal(
			"1|2026-07-20 14:30:00;2|2026-07-21 00:00:00;3|2026-07-22 18:00:00",
			await ScalarAsync(connection, "SELECT group_concat(Id || '|' || RecurrenceId, ';') FROM (SELECT * FROM Eventives ORDER BY Id)"));
		Assert.Equal("1", await ScalarAsync(connection, "SELECT \"notnull\" FROM pragma_table_info('Eventives') WHERE name = 'RecurrenceId'"));
	}

	public void Dispose()
	{
		if (File.Exists(databasePath))
		{
			File.Delete(databasePath);
		}
	}

	// Foreign keys are off so the seed rows need no owners; pooling is off so the database file can be deleted
	// afterwards.
	private PlainfraContext CreateContext() => new(new DbContextOptionsBuilder<PlainfraContext>()
		.UseSqlite($"Data Source={databasePath};Foreign Keys=False;Pooling=False")
		.Options);

	private async Task MigrateAsync(string targetMigration)
	{
		await using var context = CreateContext();
		await context.GetService<IMigrator>().MigrateAsync(targetMigration, Ct);
	}

	/// <summary>
	/// Builds the schema as it stood before manual eventives and writes a moved, timed fate eventive and an all-day
	/// objective-due eventive in that shape, in the <c>yyyy-MM-dd HH:mm:ss</c> text form EF writes.
	/// </summary>
	private async Task SeedPreMigrationRowsAsync()
	{
		await MigrateAsync(PreviousMigration);

		await ExecuteAsync("""
			INSERT INTO Eventives (Id, FateId, ObjectiveId, RecurrenceId, Resolution, Epoch_Moment, Epoch_Granularity, Epoch_Duration) VALUES
				(1, 'f-fate', NULL, '2026-07-20 14:30:00', 'Missed', '2026-07-20 15:30:00', 'Minute', '1h'),
				(2, NULL, 'o-objective', '2026-07-21 00:00:00', 'Pending', '2026-07-21 00:00:00', 'Day', NULL);
			""");
	}

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
