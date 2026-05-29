namespace Pleiades.Vault.Watcher;

/// <summary>
/// Captures decision inputs forwarded to a storage-mode policy service.
/// </summary>
public sealed record VaultStorageModeDecisionContext(
	VaultPathSyncModel Model,
	string? PathId,
	string PathTitle,
	IReadOnlyList<string> IssueMessages,
	ISet<string> KnownIds,
	bool RequiresCallerInput);
