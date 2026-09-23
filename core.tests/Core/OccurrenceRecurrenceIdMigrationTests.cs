using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Exercises the <c>OccurrenceRecurrenceId</c> migration's SQL against rows written in the pre-migration shape —
/// something a fresh-schema test vault never does. Occurrence slots collapse into one RECURRENCE-ID moment,
/// dependency slots do the same per side, and an attentive's <c>PeriodEndDate</c> folds into its epoch duration;
/// <c>Down</c> splits them back.
/// </summary>
public sealed class OccurrenceRecurrenceIdMigrationTests : IDisposable
{
	private const string PreviousMigration = "20260922124840_DueMoment";
	private const string ThisMigration = "20260923220349_OccurrenceRecurrenceId";

	private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"p7t-migration-{Guid.NewGuid():N}.db");

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Slots_collapse_into_one_recurrence_id_and_periods_fold_into_the_duration()
	{
		await SeedPreMigrationRowsAsync();
		await MigrateAsync(ThisMigration);

		await using var context = CreateContext();

		// Equality lookups find the migrated rows: the backfilled text is exactly the form EF writes and binds.
		var timed = await context.Attentives.IgnoreAutoIncludes().SingleAsync(item => item.RecurrenceId == new DateTime(2026, 7, 20, 14, 30, 0), Ct);
		Assert.Equal("r-timed", timed.DecreeId);
		Assert.Null(timed.Epoch.Duration);
		var allDay = await context.Attentives.IgnoreAutoIncludes().SingleAsync(item => item.RecurrenceId == new DateTime(2026, 7, 21), Ct);
		Assert.Equal("r-allday", allDay.DecreeId);

		// A super-day period becomes the duration in whole days, so a 61-day Pleiadean month still ends where it
		// did rather than a nominal month later.
		var month = await context.Attentives.IgnoreAutoIncludes().SingleAsync(item => item.DecreeId == "r-month", Ct);
		Assert.Equal("61d", month.Epoch.Duration);
		Assert.Equal(new DateTime(2026, 9, 22), month.Epoch.EndMoment);
		var week = await context.Attentives.IgnoreAutoIncludes().SingleAsync(item => item.DecreeId == "r-week", Ct);
		Assert.Equal("7d", week.Epoch.Duration);

		Assert.Equal(1, await context.Eventives.IgnoreAutoIncludes().CountAsync(item => item.FateId == "e-timed" && item.RecurrenceId == new DateTime(2026, 7, 20, 9, 0, 0), Ct));
		Assert.Equal(1, await context.Eventives.IgnoreAutoIncludes().CountAsync(item => item.ObjectiveId == "j-due" && item.RecurrenceId == new DateTime(2026, 7, 22), Ct));

		// Each dependency side keeps its own slot: a source slot stays the source's, a target slot the target's.
		var fromOccurrence = await context.Dependencies.SingleAsync(item => item.SourceId == "e-timed", Ct);
		Assert.Equal(new DateTime(2026, 7, 20, 9, 0, 0), fromOccurrence.SourceRecurrenceId);
		Assert.Null(fromOccurrence.TargetRecurrenceId);
		var toOccurrence = await context.Dependencies.SingleAsync(item => item.TargetRecurrenceId == new DateTime(2026, 7, 25), Ct);
		Assert.Equal("d-source", toOccurrence.SourceId);
		Assert.Null(toOccurrence.SourceRecurrenceId);
	}

	[Fact]
	public async Task Down_splits_the_slots_and_restores_the_period()
	{
		await SeedPreMigrationRowsAsync();
		await MigrateAsync(ThisMigration);
		await MigrateAsync(PreviousMigration);

		await using var context = CreateContext();
		var connection = context.Database.GetDbConnection();
		await connection.OpenAsync(Ct);

		// A timed slot splits into its date and time; a midnight slot back into a time-less (all-day) date.
		Assert.Equal("2026-07-20|14:30:00", await ScalarAsync(connection, "SELECT RecurrenceDate || '|' || RecurrenceTime FROM Attentives WHERE DecreeId = 'r-timed'"));
		Assert.Equal("2026-07-21|", await ScalarAsync(connection, "SELECT RecurrenceDate || '|' || COALESCE(RecurrenceTime, '') FROM Attentives WHERE DecreeId = 'r-allday'"));

		// The whole-days duration moves back to PeriodEndDate.
		Assert.Equal("2026-09-22|", await ScalarAsync(connection, "SELECT PeriodEndDate || '|' || COALESCE(Epoch_Duration, '') FROM Attentives WHERE DecreeId = 'r-month'"));

		Assert.Equal("2026-07-25||", await ScalarAsync(connection,
			"SELECT TargetRecurrenceDate || '|' || COALESCE(TargetRecurrenceTime, '') || '|' || COALESCE(SourceRecurrenceDate, '') FROM Dependencies WHERE SourceId = 'd-source'"));
	}

	public void Dispose()
	{
		if (File.Exists(databasePath))
		{
			File.Delete(databasePath);
		}
	}

	// Foreign keys are off because the seeded occurrences reference owners the test never creates; pooling is off
	// so the database file can be deleted afterwards.
	private PlainfraContext CreateContext() => new(new DbContextOptionsBuilder<PlainfraContext>()
		.UseSqlite($"Data Source={databasePath};Foreign Keys=False;Pooling=False")
		.Options);

	private async Task MigrateAsync(string targetMigration)
	{
		await using var context = CreateContext();
		await context.GetService<IMigrator>().MigrateAsync(targetMigration, Ct);
	}

	/// <summary>
	/// Builds the schema as it stood before this migration and writes occurrences and dependencies in that shape:
	/// timed and all-day slots, super-day periods (a 61-day Pleiadean month and a week), and dependency edges with a
	/// slot on either side.
	/// </summary>
	private async Task SeedPreMigrationRowsAsync()
	{
		await MigrateAsync(PreviousMigration);

		await using var context = CreateContext();
		await context.Database.ExecuteSqlRawAsync("""
			INSERT INTO Attentives (DecreeId, RecurrenceDate, RecurrenceTime, PeriodEndDate, Resolution, Epoch_Moment, Epoch_Granularity) VALUES
				('r-timed', '2026-07-20', '14:30:00', NULL, 'Pending', '2026-07-20 14:30:00', 'Minute'),
				('r-allday', '2026-07-21', NULL, NULL, 'Pending', '2026-07-21 00:00:00', 'Day'),
				('r-month', '2026-07-23', NULL, '2026-09-22', 'Pending', '2026-07-23 00:00:00', 'Month'),
				('r-week', '2026-08-01', NULL, '2026-08-08', 'Pending', '2026-08-01 00:00:00', 'Week');
			INSERT INTO Eventives (FateId, ObjectiveId, RecurrenceDate, RecurrenceTime, Resolution, Epoch_Moment, Epoch_Granularity) VALUES
				('e-timed', NULL, '2026-07-20', '09:00:00', 'Pending', '2026-07-20 09:00:00', 'Minute'),
				(NULL, 'j-due', '2026-07-22', NULL, 'Pending', '2026-07-22 00:00:00', 'Day');
			INSERT INTO Dependencies (SourceKind, SourceId, SourceRecurrenceDate, SourceRecurrenceTime, TargetKind, TargetId, TargetRecurrenceDate, TargetRecurrenceTime, Satisfied) VALUES
				('Eventive', 'e-timed', '2026-07-20', '09:00:00', 'Directive', 'd-target', NULL, NULL, 0),
				('Directive', 'd-source', NULL, NULL, 'Eventive', 'e-timed', '2026-07-25', NULL, 0);
			""", Ct);
	}

	private static async Task<string?> ScalarAsync(DbConnection connection, string sql)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = sql;
		return Convert.ToString(await command.ExecuteScalarAsync(Ct), CultureInfo.InvariantCulture);
	}
}
