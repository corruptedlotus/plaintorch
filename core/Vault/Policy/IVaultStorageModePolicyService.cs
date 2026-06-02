using Pleiades.Vault.Watcher;

namespace Pleiades.Vault.Policy;

/// <summary>
/// Defines storage-mode-specific watcher policy behavior for path ownership and reconciliation decisions.
/// </summary>
public interface IVaultStorageModePolicyService
{
	/// <summary>
	/// Gets the storage mode served by this policy.
	/// </summary>
	VaultStorageMode Mode { get; }

	/// <summary>
	/// Determines whether a path can be resolved to an inspectable markdown path for this model.
	/// </summary>
	bool TryResolveWatchPath(VaultPathSyncModel model, string fullPath, bool isDirectoryEvent, out string? inspectPath);

	/// <summary>
	/// Determines whether the markdown content/path should be considered as belonging to the specified model.
	/// </summary>
	Task<bool> BelongsToModelAsync(VaultPathSyncModel model, string fullPath, string markdown, CancellationToken cancellationToken);

	/// <summary>
	/// Decides watcher reconciliation action for the specified mode.
	/// </summary>
	(VaultSyncAction Action, string Reason) Decide(VaultStorageModeDecisionContext context);

	/// <summary>
	/// Resolves fallback identity used during relocation reconciliation when the old path lacks direct PUCK identity.
	/// </summary>
	string? ResolveRelocationOldIdFallback(VaultPathSyncModel model, string? oldPathId, string? newPathId);
}
