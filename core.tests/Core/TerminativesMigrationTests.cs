using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Exercises the <c>Terminatives</c> migration of PEP103 Patch103.7 against rows written before it. Each objective's due
/// eventives become its one terminative, on its current due; eventives are left to fates, and the edges that named an
/// objective's due eventive go. The new table backs its identity: one owner per row, one row per owner. Going back down
/// turns an objective's terminative into an all-day due eventive again.
/// </summary>
public sealed class TerminativesMigrationTests : IDisposable
{
	private const string PreviousMigration = "20261011153000_FateStoppedStatus";

	private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"p7t-migration-{Guid.NewGuid():N}.db");

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Due_eventives_become_terminatives_on_the_current_due()
	{
		await SeedPreMigrationRowsAsync();
		await MigrateAsync();

		// A done objective's all-day due: resolved, not ignored, an all-day occurrence with no zone.
		Assert.Equal("Resolved|0|2026-07-21 00:00:00|Day||2026-07-21 00:00:00", await ScalarAsync(
			"SELECT Resolution || '|' || Ignored || '|' || Epoch_Moment || '|' || Epoch_Granularity || '|' || COALESCE(Epoch_TimeZone, '') || '|' || RecurrenceId FROM Terminatives WHERE ObjectiveId = 'j-done'"));
		// An open objective whose due moved to a timed, zoned moment since its eventive hardened: the terminative sits on the
		// due, and the missed eventive (one of two) leaves it ignored.
		Assert.Equal("Pending|1|2026-07-25 14:30:00|Minute|Europe/Berlin", await ScalarAsync(
			"SELECT Resolution || '|' || Ignored || '|' || Epoch_Moment || '|' || Epoch_Granularity || '|' || Epoch_TimeZone FROM Terminatives WHERE ObjectiveId = 'j-open'"));
		// An objective whose due was cleared keeps no terminative.
		Assert.Equal("2", await ScalarAsync("SELECT COUNT(*) FROM Terminatives"));

		// Eventives are a fate's alone now.
		Assert.Equal("1", await ScalarAsync("SELECT group_concat(Id) FROM Eventives"));
		Assert.Equal("1", await ScalarAsync("SELECT \"notnull\" FROM pragma_table_info('Eventives') WHERE name = 'FateId'"));
		Assert.Equal("0", await ScalarAsync("SELECT COUNT(*) FROM pragma_table_info('Eventives') WHERE name = 'ObjectiveId'"));

		// The edge that named an objective's due eventive is gone; the fate's stays.
		Assert.Equal("e-fate", await ScalarAsync("SELECT group_concat(SourceId) FROM Dependencies"));

		await using (var context = CreateContext())
		{
			var terminative = await context.Terminatives.AsNoTracking().SingleAsync(item => item.ObjectiveId == "j-open", Ct);
			Assert.Equal(new DateTime(2026, 7, 25, 14, 30, 0), terminative.RecurrenceId);
			Assert.True(terminative.Ignored);
		}
	}

	[Fact]
	public async Task The_table_backs_one_owner_per_row_and_one_row_per_owner()
	{
		await SeedPreMigrationRowsAsync();
		await MigrateAsync();

		await Assert.ThrowsAnyAsync<DbException>(() => ExecuteAsync("""
			INSERT INTO Terminatives (ObjectiveId, Resolution, Ignored, Epoch_Moment, Epoch_Granularity, RecurrenceId)
				VALUES ('j-done', 'Pending', 0, '2026-07-21 00:00:00', 'Day', '2026-07-21 00:00:00');
			"""));
		await Assert.ThrowsAnyAsync<DbException>(() => ExecuteAsync("""
			INSERT INTO Terminatives (ObjectiveId, CheckpointId, Resolution, Ignored, Epoch_Moment, Epoch_Granularity, RecurrenceId)
				VALUES ('j-cleared', 'c-gate', 'Pending', 0, '2026-07-21 00:00:00', 'Day', '2026-07-21 00:00:00');
			"""));
		await Assert.ThrowsAnyAsync<DbException>(() => ExecuteAsync("""
			INSERT INTO Terminatives (Resolution, Ignored, Epoch_Moment, Epoch_Granularity, RecurrenceId)
				VALUES ('Pending', 0, '2026-07-21 00:00:00', 'Day', '2026-07-21 00:00:00');
			"""));

		// Deleting an owner deletes its terminative.
		await ExecuteAsync("DELETE FROM Incentives WHERE Id = 'j-done';");
		Assert.Equal("j-open", await ScalarAsync("SELECT group_concat(ObjectiveId) FROM Terminatives"));
	}

	[Fact]
	public async Task Down_turns_an_objectives_terminative_back_into_a_due_eventive()
	{
		await SeedPreMigrationRowsAsync();
		await MigrateAsync();
		await ExecuteAsync("""
			INSERT INTO Directives (Id, Title, Discriminator, Status, Tags) VALUES ('A-campaign', 'Campaign', 'StellarDirective', 'Planned', '[]');
			INSERT INTO Terminatives (DirectiveId, Resolution, Ignored, Epoch_Moment, Epoch_Granularity, RecurrenceId)
				VALUES ('A-campaign', 'Pending', 0, '2026-08-01 00:00:00', 'Day', '2026-08-01 00:00:00');
			""");

		await MigrateAsync(PreviousMigration);

		Assert.Equal("0", await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE name = 'Terminatives'"));
		Assert.Equal("0", await ScalarAsync("SELECT \"notnull\" FROM pragma_table_info('Eventives') WHERE name = 'FateId'"));
		Assert.Equal(
			"e-fate||Pending;|j-done|Pending|2026-07-21 00:00:00|Day;|j-open|Cancelled|2026-07-25 00:00:00|Day",
			await ScalarAsync("""
				SELECT group_concat(COALESCE(FateId, '') || '|' || COALESCE(ObjectiveId, '') || '|' || Resolution
					|| CASE WHEN ObjectiveId IS NULL THEN '' ELSE '|' || RecurrenceId || '|' || Epoch_Granularity END, ';')
				FROM (SELECT * FROM Eventives ORDER BY Id)
				"""));
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
	/// Builds the schema as it stood before terminatives and writes, in that shape, a fate eventive and three objectives'
	/// due eventives: a done objective's on its all-day due, two of an open objective whose due has since moved to a timed
	/// moment (one of them missed), and one of an objective whose due was cleared. Two edges name eventives: the fate's and
	/// the open objective's.
	/// </summary>
	private async Task SeedPreMigrationRowsAsync()
	{
		await MigrateAsync(PreviousMigration);
		await ExecuteAsync("""
			INSERT INTO Incentives (Id, Title, Discriminator, FateStatus) VALUES ('e-fate', 'Fate', 'Fate', 'Active');
			INSERT INTO Incentives (Id, Title, Discriminator, Status, Objective_College, CelestronValue, Due_Moment, Due_TimeZone) VALUES
				('j-done', 'Done', 'Objective', 'Done', 'Unspecified', 0, '2026-07-21 00:00:00', NULL),
				('j-open', 'Open', 'Objective', 'Polaris', 'Unspecified', 0, '2026-07-25 14:30:00', 'Europe/Berlin'),
				('j-cleared', 'Cleared', 'Objective', 'Standby', 'Unspecified', 0, NULL, NULL);
			INSERT INTO Checkpoints (Id, Title, TollPaid, Unlocked) VALUES ('c-gate', 'Gate', 0, 0);
			INSERT INTO Eventives (Id, FateId, ObjectiveId, RecurrenceId, Resolution, Epoch_Moment, Epoch_Granularity) VALUES
				(1, 'e-fate', NULL, '2026-07-20 09:00:00', 'Pending', '2026-07-20 09:00:00', 'Minute'),
				(2, NULL, 'j-done', '2026-07-21 00:00:00', 'Pending', '2026-07-21 00:00:00', 'Day'),
				(3, NULL, 'j-open', '2026-07-22 00:00:00', 'Missed', '2026-07-22 00:00:00', 'Day'),
				(4, NULL, 'j-open', '2026-07-23 00:00:00', 'Pending', '2026-07-23 00:00:00', 'Day'),
				(5, NULL, 'j-cleared', '2026-07-24 00:00:00', 'Pending', '2026-07-24 00:00:00', 'Day');
			INSERT INTO Dependencies (SourceKind, SourceId, SourceRecurrenceId, TargetKind, TargetId, Satisfied) VALUES
				('Eventive', 'e-fate', '2026-07-20 09:00:00', 'Objective', 'j-cleared', 0),
				('Eventive', 'j-open', '2026-07-22 00:00:00', 'Objective', 'j-cleared', 0);
			""");
	}

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
