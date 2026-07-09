using System.Diagnostics;
using Pleiades.Vault.Database;

namespace Pleiades.Vault.Migration;

/// <summary>
/// Applies pending vault migrations in order, advancing the stored vault schema version toward the current version.
/// </summary>
/// <remarks>
/// Runs during vault initialisation, after the database schema has been migrated. Each migration advances the vault by
/// one step; the stored version is written only after a step completes, so a crashed or re-run initialisation resumes
/// safely from the last committed version.
/// </remarks>
public sealed class VaultMigrationRunner(
	VaultVersionService versionService,
	IEnumerable<IVaultMigration> migrations,
	PlainfraContext context,
	VaultAuditLogService auditLogService,
	ILogger<VaultMigrationRunner> logger)
{
	/// <summary>
	/// Computes the ordered list of migrations that would run for the current stored version, without applying them.
	/// </summary>
	public IReadOnlyList<IVaultMigration> GetPendingPlan()
	{
		var current = versionService.CurrentVersion;
		var ordered = OrderedMigrations();
		var pending = new List<IVaultMigration>();
		var version = versionService.GetStoredVersion();
		while (version < current)
		{
			var migration = ordered.FirstOrDefault(candidate => candidate.FromVersion == version);
			if (migration is null)
			{
				break;
			}

			pending.Add(migration);
			version = migration.ToVersion;
		}

		return pending;
	}

	/// <summary>
	/// Applies all pending vault migrations.
	/// </summary>
	public async Task RunAsync(CancellationToken cancellationToken = default)
	{
		var stored = versionService.GetStoredVersion();
		var current = versionService.CurrentVersion;

		if (stored == current)
		{
			logger.LogDebug("Vault schema is current at v{Version}; no vault migration needed.", stored);
			return;
		}

		if (stored > current)
		{
			logger.LogWarning("Vault schema v{Stored} is newer than the engine's v{Current}; skipping vault migration.", stored, current);
			return;
		}

		var ordered = OrderedMigrations();
		var version = stored;
		while (version < current)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var migration = ordered.FirstOrDefault(candidate => candidate.FromVersion == version)
				?? throw new InvalidOperationException($"No vault migration is registered from version {version} toward current version {current}.");

			logger.LogInformation(
				"Applying vault migration {Id} (v{From} -> v{To}): {Description}",
				migration.Id,
				migration.FromVersion,
				migration.ToVersion,
				migration.Description);

			var stopwatch = Stopwatch.StartNew();
			VaultMigrationOutcome outcome;
			try
			{
				outcome = await migration.ApplyAsync(cancellationToken);
			}
			catch (Exception exception)
			{
				stopwatch.Stop();
				await RecordHistoryAsync(migration, stopwatch.ElapsedMilliseconds, new VaultMigrationOutcome(0, 0, 0, 0, 0), "failed", cancellationToken);
				logger.LogError(exception, "Vault migration {Id} failed; vault schema left at v{Version}.", migration.Id, version);
				throw;
			}

			stopwatch.Stop();
			versionService.SetStoredVersion(migration.ToVersion);
			await RecordHistoryAsync(
				migration,
				stopwatch.ElapsedMilliseconds,
				outcome,
				outcome.Conflicts > 0 ? "completed-with-conflicts" : "completed",
				cancellationToken);

			logger.LogInformation(
				"Vault migration {Id} complete: loaded {Loaded}, rewritten {Rewritten}, archived {Archived}, imported {Imported}, conflicts {Conflicts}. Vault now v{Version}.",
				migration.Id,
				outcome.Loaded,
				outcome.Rewritten,
				outcome.Archived,
				outcome.Imported,
				outcome.Conflicts,
				migration.ToVersion);

			version = migration.ToVersion;
		}
	}

	private List<IVaultMigration> OrderedMigrations()
	{
		return migrations
			.OrderBy(migration => migration.FromVersion)
			.ThenBy(migration => migration.ToVersion)
			.ToList();
	}

	private async Task RecordHistoryAsync(IVaultMigration migration, long durationMs, VaultMigrationOutcome outcome, string status, CancellationToken cancellationToken)
	{
		context.VaultMigrationHistory.Add(new VaultMigrationHistory
		{
			MigrationId = migration.Id,
			FromVersion = migration.FromVersion,
			ToVersion = migration.ToVersion,
			AppliedUtc = DateTimeOffset.UtcNow,
			AppliedBy = Environment.UserName,
			DurationMs = durationMs,
			EntitiesLoaded = outcome.Loaded,
			FilesRewritten = outcome.Rewritten,
			FilesArchived = outcome.Archived,
			EntitiesImported = outcome.Imported,
			Conflicts = outcome.Conflicts,
			Outcome = status,
		});
		await context.SaveChangesAsync(cancellationToken);

		await auditLogService.WriteAsync(
			"migration",
			"applied",
			subjectId: migration.Id,
			subjectTitle: migration.Description,
			details: new { migration.Id, migration.FromVersion, migration.ToVersion, status, durationMs, outcome },
			cancellationToken: cancellationToken);
	}
}
