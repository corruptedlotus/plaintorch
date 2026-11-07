using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Exercises the <c>OnboardStatus</c> migration's SQL (PEP103 Patch103.3): an objective stored as <c>Onrush</c> reads
/// <c>Onboard</c>, every other status and every other kind is left alone, and <c>Down</c> writes the old name back.
/// </summary>
public sealed class OnboardStatusMigrationTests : IDisposable
{
	private const string PreviousMigration = "20261005044650_LifecycleStates";

	private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"p7t-migration-{Guid.NewGuid():N}.db");

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task An_onrush_objective_becomes_onboard_and_nothing_else_moves()
	{
		await SeedAsync();
		await MigrateAsync();

		await using var context = CreateContext();
		var connection = context.Database.GetDbConnection();
		await connection.OpenAsync(Ct);
		Assert.Equal("Onboard", await StatusOfAsync(connection, "QUEST01"));
		Assert.Equal("Standby", await StatusOfAsync(connection, "QUEST02"));
		Assert.Equal("Active", await FateStatusOfAsync(connection, "f00000001"));

		// The rows read through the model as the renamed status.
		Assert.Equal(Pleiades.Orchestration.ObjectiveStatus.Onboard, (await context.Objectives.AsNoTracking().IgnoreAutoIncludes().SingleAsync(objective => objective.Id == "QUEST01", Ct)).Status);
	}

	[Fact]
	public async Task Down_writes_the_old_name_back()
	{
		await SeedAsync();
		await MigrateAsync();
		await MigrateAsync(PreviousMigration);

		await using var context = CreateContext();
		var connection = context.Database.GetDbConnection();
		await connection.OpenAsync(Ct);
		Assert.Equal("Onrush", await StatusOfAsync(connection, "QUEST01"));
		Assert.Equal("Standby", await StatusOfAsync(connection, "QUEST02"));
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
	/// Builds the schema as it stood before this migration and writes an objective on board a sprint under the old name, one
	/// on standby, and a fate.
	/// </summary>
	private async Task SeedAsync()
	{
		await MigrateAsync(PreviousMigration);

		await using var context = CreateContext();
		await context.Database.ExecuteSqlRawAsync("""
			INSERT INTO Incentives (Id, Title, Discriminator, Status, Objective_College, CelestronValue) VALUES
				('QUEST01', 'Sprint work', 'Objective', 'Onrush', 'Unspecified', 0),
				('QUEST02', 'Waiting work', 'Objective', 'Standby', 'Unspecified', 0);
			INSERT INTO Incentives (Id, Title, Discriminator, FateStatus) VALUES
				('f00000001', 'Fate', 'Fate', 'Active');
			""", Ct);
	}

	private static async Task<string?> StatusOfAsync(DbConnection connection, string id)
		=> await ScalarAsync(connection, $"SELECT Status FROM Incentives WHERE Id = '{id}'");

	private static async Task<string?> FateStatusOfAsync(DbConnection connection, string id)
		=> await ScalarAsync(connection, $"SELECT FateStatus FROM Incentives WHERE Id = '{id}'");

	private static async Task<string?> ScalarAsync(DbConnection connection, string sql)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = sql;
		return Convert.ToString(await command.ExecuteScalarAsync(Ct), CultureInfo.InvariantCulture);
	}
}
