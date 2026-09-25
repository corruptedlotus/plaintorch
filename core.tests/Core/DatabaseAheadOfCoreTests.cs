using System.Security.Cryptography;
using System.Text.Json;
using A11d.Module;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pleiades.Plaintorch;
using Pleiades.Plaintorch.Hosting;
using Pleiades.Resources;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// A vault whose database was migrated by a newer core — its <c>__EFMigrationsHistory</c> names a migration this core's
/// assembly does not contain — is refused at activation, with an actionable message and the database left untouched.
/// Without the guard <c>Migrate</c> applied nothing, the core reported <c>Active</c>, and every query touching a changed
/// table then failed (a missing column on every save, via the dependency reconciler).
/// </summary>
public sealed class DatabaseAheadOfCoreTests : VaultTestBase
{
	private const string UnknownMigration = "20991231235959_FromANewerCore";

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Initialization_refuses_a_database_migrated_by_a_newer_core_and_leaves_it_untouched()
	{
		await RecordAppliedMigrationsAsync(UnknownMigration);
		var before = await SnapshotDatabaseAsync();

		var exception = await Assert.ThrowsAsync<VaultDatabaseAheadOfCoreException>(() =>
			Vault.WithScopeAsync(services => services.GetRequiredService<PlaintorchEngine>().InitializeVaultAsync(Ct)));

		Assert.Equal(VaultMessages.Activation.DatabaseAheadOfCore(Vault.Layout.VaultRoot, UnknownMigration), exception.Message);
		Assert.Contains(Vault.Layout.VaultRoot, exception.Message);
		Assert.Contains(UnknownMigration, exception.Message);
		Assert.Equal(Vault.Layout.VaultRoot, exception.VaultPath);
		Assert.Equal([UnknownMigration], exception.UnknownMigrations);
		AssertUnchanged(before, await SnapshotDatabaseAsync());
	}

	[Fact]
	public async Task The_refusal_names_the_newest_of_several_unknown_migrations()
	{
		const string older = "20990101000000_AlsoFromANewerCore";
		await RecordAppliedMigrationsAsync(UnknownMigration, older);

		var exception = await Assert.ThrowsAsync<VaultDatabaseAheadOfCoreException>(() =>
			Vault.WithScopeAsync(services => services.GetRequiredService<PlaintorchEngine>().InitializeVaultAsync(Ct)));

		Assert.Equal([older, UnknownMigration], exception.UnknownMigrations);
		Assert.Equal(UnknownMigration, exception.NewestUnknownMigration);
		Assert.Equal(VaultMessages.Activation.DatabaseAheadOfCore(Vault.Layout.VaultRoot, UnknownMigration), exception.Message);
	}

	[Fact]
	public async Task A_database_this_core_migrated_still_initializes()
	{
		// The guard only refuses what is unknown: re-initializing a vault this core migrated (every applied id known) passes.
		await Vault.WithScopeAsync(services => services.GetRequiredService<PlaintorchEngine>().InitializeVaultAsync(Ct));

		var applied = await Vault.QueryAsync(context => context.Database.GetAppliedMigrationsAsync(Ct));
		var known = await Vault.QueryAsync(context => Task.FromResult(context.Database.GetMigrations()));
		Assert.Equal(known, applied);
	}

	[Fact]
	public async Task The_core_reports_the_refusal_as_a_failed_activation_and_never_serves_the_vault()
	{
		await RecordAppliedMigrationsAsync(UnknownMigration);
		var before = await SnapshotDatabaseAsync();

		// A daemon exactly as `serve` starts one: no vault bound, activation driven by the coordinator.
		var userLayout = PlaintorchUserLayout.CreateEphemeral();
		var builder = WebApplication.CreateBuilder();
		builder.Logging.ClearProviders();
		builder.Services.AddSingleton(userLayout);
		await using var daemon = builder.Install<PLAINTORCH>().Build();

		var hostState = daemon.Services.GetRequiredService<PlaintorchHostState>();
		var session = daemon.Services.GetRequiredService<ActiveVaultSession>();
		var coordinator = daemon.Services.GetServices<IHostedService>().OfType<PlaintorchCoreService>().Single();
		var settled = new TaskCompletionSource<PlaintorchHostStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
		hostState.Changed += status =>
		{
			if (status.Phase is PlaintorchHostPhase.Failed or PlaintorchHostPhase.Active)
			{
				settled.TrySetResult(status);
			}
		};

		// The coordinator prefers PLAINTORCH_VAULT_PATH over the profile's config (it is how `serve --vault` targets a
		// vault), so pointing it at this vault also keeps a developer's own override from sending the daemon elsewhere.
		// Nothing else a test builds reads it at run time.
		var previousOverride = Environment.GetEnvironmentVariable("PLAINTORCH_VAULT_PATH");
		Environment.SetEnvironmentVariable("PLAINTORCH_VAULT_PATH", Vault.VaultRoot);
		PlaintorchHostStatus status;
		try
		{
			await coordinator.StartAsync(Ct);
			status = await settled.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
		}
		finally
		{
			await coordinator.StopAsync(CancellationToken.None);
			Environment.SetEnvironmentVariable("PLAINTORCH_VAULT_PATH", previousOverride);
			userLayout.Cleanup();
		}

		var expected = VaultMessages.Activation.DatabaseAheadOfCore(Vault.Layout.VaultRoot, UnknownMigration);
		Assert.Equal(PlaintorchHostPhase.Failed, status.Phase);
		Assert.Equal(expected, status.Message);
		Assert.Equal(Vault.Layout.VaultRoot, status.VaultPath);
		Assert.False(session.IsActive);

		// /healthz reports hostState.Current and session.IsActive; the spawn-mode stream serializes each transition.
		Assert.Equal(PlaintorchHostPhase.Failed, hostState.Current.Phase);
		Assert.Equal(expected, hostState.Current.Message);
		using var streamed = JsonDocument.Parse(JsonSerializer.Serialize(PlaintorchHostStatusStream.ToEvent(status), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
		Assert.Equal("Failed", streamed.RootElement.GetProperty("phase").GetString());
		Assert.Equal(expected, streamed.RootElement.GetProperty("message").GetString());

		AssertUnchanged(before, await SnapshotDatabaseAsync());
	}

	/// <summary>Records migrations as applied, as a newer core's <c>Migrate</c> would have left them.</summary>
	private Task RecordAppliedMigrationsAsync(params string[] migrationIds)
	{
		return Vault.QueryAsync(async context =>
		{
			foreach (var migrationId in migrationIds)
			{
				await context.Database.ExecuteSqlAsync($"INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ({migrationId}, '99.0.0')", Ct);
			}

			return 0;
		});
	}

	/// <summary>
	/// Captures the database as it rests on disk: the migration history, the schema, and a hash of the file once the
	/// write-ahead log has been folded into it (so the file alone is the whole database and any write shows up).
	/// </summary>
	private async Task<DatabaseSnapshot> SnapshotDatabaseAsync()
	{
		await using var connection = new SqliteConnection($"Data Source={Vault.Layout.DatabasePath};Pooling=False");
		await connection.OpenAsync(Ct);
		// Its first column is "busy": non-zero means a reader blocked the fold, and the file alone would not be the database.
		Assert.Equal("0", (await ReadColumnAsync(connection, "PRAGMA wal_checkpoint(TRUNCATE)"))[0]);
		var history = await ReadColumnAsync(connection, "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId");
		var schema = await ReadColumnAsync(connection, "SELECT type || ' ' || name || ': ' || COALESCE(sql, '') FROM sqlite_master ORDER BY type, name");
		var hash = Convert.ToHexString(SHA256.HashData(await ReadSharedAsync(Vault.Layout.DatabasePath)));
		return new DatabaseSnapshot(history, schema, hash);
	}

	private static void AssertUnchanged(DatabaseSnapshot before, DatabaseSnapshot after)
	{
		Assert.Equal(before.History, after.History);
		Assert.Equal(before.Schema, after.Schema);
		Assert.Equal(before.FileHash, after.FileHash);
	}

	private static async Task<IReadOnlyList<string>> ReadColumnAsync(SqliteConnection connection, string sql)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = sql;
		await using var reader = await command.ExecuteReaderAsync(Ct);
		var values = new List<string>();
		while (await reader.ReadAsync(Ct))
		{
			values.Add(reader.GetString(0));
		}

		return values;
	}

	// The file is open (the snapshot connection, pooled host connections), so it is read with shared access.
	private static async Task<byte[]> ReadSharedAsync(string path)
	{
		await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		using var buffer = new MemoryStream();
		await stream.CopyToAsync(buffer, Ct);
		return buffer.ToArray();
	}

	private sealed record DatabaseSnapshot(IReadOnlyList<string> History, IReadOnlyList<string> Schema, string FileHash);
}
