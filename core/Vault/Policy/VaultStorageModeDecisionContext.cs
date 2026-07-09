using Pleiades.Vault.Watcher;

namespace Pleiades.Vault.Policy;

/// <summary>
/// Captures decision inputs forwarded to a storage-mode policy service.
/// </summary>
/// <param name="BoundaryBegun">
/// Indicates whether the entity's synchronization boundary has begun. Only meaningful for
/// <see cref="VaultStorageMode.Implicit"/>; defaults to <see langword="true"/> for all other modes so their
/// reconciliation behavior is unaffected.
/// </param>
public sealed record VaultStorageModeDecisionContext(
	VaultPathSyncModel Model,
	string? PathId,
	string PathTitle,
	IReadOnlyList<string> IssueMessages,
	ISet<string> KnownIds,
	bool FileExists,
	bool RequiresCallerInput,
	bool BoundaryBegun = true);
