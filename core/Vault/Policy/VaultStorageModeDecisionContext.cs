using Pleiades.Vault.Watcher;

namespace Pleiades.Vault.Policy;

/// <summary>
/// Captures decision inputs forwarded to a storage-mode policy service.
/// </summary>
public sealed record VaultStorageModeDecisionContext(
	VaultPathSyncModel Model,
	string? PathId,
	string PathTitle,
	IReadOnlyList<string> IssueMessages,
	ISet<string> KnownIds,
	bool FileExists,
	bool RequiresCallerInput);
