namespace Pleiades.Vault.Watcher;

/// <summary>
/// Describes the provisional reconciliation action suggested for a discovered vault-backed markdown path.
/// </summary>
public enum VaultSyncAction
{
	/// <summary>
	/// Ignore the candidate because no action is currently safe or necessary.
	/// </summary>
	Ignore,

	/// <summary>
	/// Create a new database entity from the file candidate.
	/// </summary>
	CreateFromFile,

	/// <summary>
	/// Update an existing database entity from the file candidate.
	/// </summary>
	UpdateFromFile,

	/// <summary>
	/// Delete the corresponding database entity because the authoritative file was removed.
	/// </summary>
	DeleteFromDatabase,

	/// <summary>
	/// Rewrite the file from canonical database state.
	/// </summary>
	RewriteFromDatabase,

	/// <summary>
	/// Purge the file because it is disallowed by policy.
	/// </summary>
	PurgeFile,

	/// <summary>
	/// Mark the candidate as requiring manual conflict resolution.
	/// </summary>
	Conflict,
}
