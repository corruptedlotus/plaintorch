namespace Pleiades.Vault.Database;

/// <summary>
/// What the vault drainer must do for a queued entity write (PEP110 Refactor BETA).
/// </summary>
public enum VaultWriteIntentKind
{
	/// <summary>
	/// Reconcile the entity's markdown file to its current database state — create, update, rename, or reparent. The
	/// drainer re-derives everything from the database and the existing file, so the intent carries no diff.
	/// </summary>
	Reconcile,

	/// <summary>
	/// Remove the entity's markdown file. The database row is already gone, so the file is located by the intent's
	/// captured identity / last-known path.
	/// </summary>
	Remove,
}
