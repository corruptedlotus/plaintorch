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
	/// Gets whether belonging is driven by identity (frontmatter PUCK) rather than path: an identity-driven entity's
	/// file may live anywhere and its title/fields come from frontmatter, not from a canonical filename. True for
	/// Freeform and Implicit. Pipeline code asks this instead of testing the mode enum.
	/// </summary>
	bool IsIdentityDriven { get; }

	/// <summary>
	/// Gets whether creating an entity materializes its file immediately. False for Implicit, which stays
	/// database-first until its synchronization boundary is begun; true for every other mode.
	/// </summary>
	bool MaterializesOnCreate { get; }

	/// <summary>
	/// Gets whether a first-appearing file begins a one-time synchronization boundary for the entity, after which the
	/// file's deletion is authoritative. True only for Implicit.
	/// </summary>
	bool BeginsSyncBoundaryOnFirstFile { get; }

	/// <summary>
	/// Gets whether the mode keeps its root a clean outward interface owned by the core, purging vault files that no
	/// longer correspond to a live entity (foreign or desynced notes). True only for Enforced.
	/// </summary>
	bool PurgesDesyncedFiles { get; }

	/// <summary>
	/// Resolves where an entity's markdown should be written. Path-bound modes always use the canonical
	/// <paramref name="defaultPath"/>; Freeform keeps a user-authored file at its authored location when that is
	/// allowed. The policy owns this so the storage pipeline never branches on the mode.
	/// </summary>
	/// <param name="entity">The entity being written.</param>
	/// <param name="defaultPath">The canonical target path the storage pipeline computed.</param>
	/// <param name="sourcePath">The file the write originated from, when any (a user-authored freeform file).</param>
	string ResolveWriteTargetPath(object entity, string defaultPath, string? sourcePath);

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
