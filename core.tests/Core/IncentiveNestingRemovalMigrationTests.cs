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
/// Exercises the <c>RemoveIncentiveNesting</c> migration (PEP102 patch 9) applied out of order. It sorts before
/// <c>DeclarativeTimeZone</c>, which adds an <c>Incentives</c> column, but the two were written on parallel branches,
/// so a database that ran <c>DeclarativeTimeZone</c> first meets the nesting removal afterwards. A table rebuild there
/// would recreate <c>Incentives</c> from the removal's own model and drop the later column with its values.
/// </summary>
/// <remarks>
/// Foreign keys stay on, as in a live vault, so the emptied parent link is proven not to hold back a delete.
/// </remarks>
public sealed class IncentiveNestingRemovalMigrationTests : IDisposable
{
	private const string BeforeEitherBranch = "20260925001846_TimeframeExclusiveAndDirectiveAvailability";
	private const string LaterIncentivesMigration = "20260930230346_DeclarativeTimeZone";

	private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"p7t-migration-{Guid.NewGuid():N}.db");

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task A_database_that_already_ran_a_later_incentives_migration_keeps_that_column()
	{
		await MigrateAsync(BeforeEitherBranch);

		await using (var context = CreateContext())
		{
			// The column DeclarativeTimeZone adds and its history row, as a database that ran it first holds them;
			// beside it, a fate nested under an objective, so the removal has a parent link to empty.
			await context.Database.ExecuteSqlRawAsync($"""
				ALTER TABLE Incentives ADD COLUMN TimeZone TEXT NULL;
				INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('{LaterIncentivesMigration}', '10.0.0');
				INSERT INTO Incentives (Id, Title, Discriminator, Status, Objective_College, CelestronValue) VALUES
					('QUEST01', 'Parent quest', 'Objective', 'Standby', 'Unspecified', 0);
				INSERT INTO Incentives (Id, Title, Discriminator, FateStatus, TimeZone, ParentIncentiveId) VALUES
					('e00000001', 'Zoned fate', 'Fate', 'Active', 'Asia/Tehran', 'QUEST01');
				""", Ct);
		}

		await MigrateAsync();

		await using var migrated = CreateContext();
		Assert.Empty(await migrated.Database.GetPendingMigrationsAsync(Ct));

		// The later column and its value come through, and the model reads them.
		var fate = await migrated.Set<Fate>().AsNoTracking().IgnoreAutoIncludes().SingleAsync(item => item.Id == "e00000001", Ct);
		Assert.Equal("Asia/Tehran", fate.TimeZone);

		// The parent link was emptied, so the former parent goes without meeting the foreign key.
		var connection = migrated.Database.GetDbConnection();
		await connection.OpenAsync(Ct);
		Assert.Equal("0", await ScalarAsync(connection, "SELECT COUNT(*) FROM Incentives WHERE ParentIncentiveId IS NOT NULL"));
		Assert.Equal(1, await migrated.Database.ExecuteSqlRawAsync("DELETE FROM Incentives WHERE Id = 'QUEST01'", Ct));
		Assert.Equal("", await ScalarAsync(connection, "SELECT group_concat(\"table\") FROM pragma_foreign_key_check"));
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

	private static async Task<string?> ScalarAsync(DbConnection connection, string sql)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = sql;
		return Convert.ToString(await command.ExecuteScalarAsync(Ct), CultureInfo.InvariantCulture);
	}
}
