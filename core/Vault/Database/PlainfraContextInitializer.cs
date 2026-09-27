using Microsoft.EntityFrameworkCore;

namespace Pleiades.Vault.Database;

/// <summary>
/// Ensures the vault-local database schema exists before the application begins normal work.
/// </summary>
/// <remarks>
/// A database may only move forward under this core: one that records migrations the core does not contain was
/// migrated by a newer core, and is refused (<see cref="VaultDatabaseAheadOfCoreException"/>) before anything writes to it.
/// Left alone, <c>Migrate</c> would find nothing to apply and the core would serve a model the schema no longer matches.
/// </remarks>
public sealed class PlainfraContextInitializer(PlainfraContext context, VaultLayout layout)
{
	/// <summary>
	/// Refuses a database migrated by a newer core, then applies pending database migrations.
	/// </summary>
	/// <exception cref="VaultDatabaseAheadOfCoreException">The database records migrations this core does not contain.</exception>
	public void Initialize()
	{
		EnsureNotAheadOfCore(context.Database.GetAppliedMigrations());
		context.Database.Migrate();
	}

	/// <summary>
	/// Refuses a database migrated by a newer core, then applies pending database migrations.
	/// </summary>
	/// <exception cref="VaultDatabaseAheadOfCoreException">The database records migrations this core does not contain.</exception>
	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		EnsureNotAheadOfCore(await context.Database.GetAppliedMigrationsAsync(cancellationToken));
		await context.Database.MigrateAsync(cancellationToken);
	}

	/// <summary>
	/// Throws when the database's migration history names a migration this core's assembly does not contain. Reading the
	/// history is the only database access: a missing database or history table simply reports none applied.
	/// </summary>
	private void EnsureNotAheadOfCore(IEnumerable<string> appliedMigrations)
	{
		var known = context.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
		// Migration ids lead with their creation timestamp, so ordinal order is chronological and the last is the newest.
		var unknown = appliedMigrations
			.Where(migration => !known.Contains(migration))
			.Order(StringComparer.Ordinal)
			.ToList();

		if (unknown.Count > 0)
		{
			throw new VaultDatabaseAheadOfCoreException(layout.VaultRoot, unknown);
		}
	}
}
