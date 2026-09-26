using Pleiades.Resources;
using Pleiades.Vault.Watcher;

namespace Pleiades.Vault.Policy;

/// <summary>
/// Implements synced storage mode policy.
/// </summary>
public sealed class SyncedVaultStorageModePolicyService : PathBoundVaultStorageModePolicyService
{
	/// <inheritdoc />
	public override VaultStorageMode Mode => VaultStorageMode.Synced;

	/// <inheritdoc />
	public override VaultSyncDecision Decide(VaultStorageModeDecisionContext context)
	{
		// A missing file has no content to judge and, without an identity, no entity to reconcile — a note deleted before
		// it was ever synced, or the primary a folder event synthesizes — so there is nothing to create, purge or hold in
		// conflict.
		if (!context.FileExists && string.IsNullOrWhiteSpace(context.PathId))
		{
			return new(VaultSyncAction.Ignore, WatcherMessages.Decisions.SyncedMissingFileUnknownEntity);
		}

		if (string.IsNullOrWhiteSpace(context.PathId) && IsUntitledPlaceholder(context.PathTitle))
		{
			return new(VaultSyncAction.Ignore, WatcherMessages.Decisions.UntitledPlaceholderIgnored);
		}

		if (string.IsNullOrWhiteSpace(context.PathId))
		{
			if (context.RequiresCallerInput)
			{
				return new(VaultSyncAction.Conflict, WatcherMessages.Decisions.MissingRequiredPuckInputConflict, VaultSyncConcern.PuckViolation);
			}

			return new(VaultSyncAction.CreateFromFile, WatcherMessages.Decisions.SyncedTitleOnlyCreated);
		}

		var exists = context.KnownIds.Contains(context.PathId);
		if (!context.FileExists)
		{
			if (exists)
			{
				return new(VaultSyncAction.DeleteFromDatabase, WatcherMessages.Decisions.SyncedDeletedFileRemovesEntity);
			}

			return new(VaultSyncAction.Ignore, WatcherMessages.Decisions.SyncedMissingFileUnknownEntity);
		}

		if (context.IssueMessages.Count > 0)
		{
			if (!exists)
			{
				return new(VaultSyncAction.Conflict, WatcherMessages.Decisions.InvalidCandidateConflict, VaultSyncConcern.MarkdownInvalid);
			}

			return new(VaultSyncAction.RewriteFromDatabase, WatcherMessages.Decisions.SyncedInvalidCandidateRewritten, VaultSyncConcern.MarkdownInvalid);
		}

		if (exists)
		{
			return new(VaultSyncAction.UpdateFromFile, WatcherMessages.Decisions.PathIdentityExists);
		}

		return new(VaultSyncAction.CreateFromFile, WatcherMessages.Decisions.SyncedFileOriginatedCreation);
	}
}
