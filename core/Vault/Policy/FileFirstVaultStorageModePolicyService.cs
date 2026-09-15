using Pleiades.Resources;
using Pleiades.Vault.Watcher;

namespace Pleiades.Vault.Policy;

/// <summary>
/// Implements file-first storage mode policy.
/// </summary>
public sealed class FileFirstVaultStorageModePolicyService : PathBoundVaultStorageModePolicyService
{
	/// <inheritdoc />
	public override VaultStorageMode Mode => VaultStorageMode.FileFirst;

	/// <inheritdoc />
	public override VaultSyncDecision Decide(VaultStorageModeDecisionContext context)
	{
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

			return new(VaultSyncAction.CreateFromFile, WatcherMessages.Decisions.FileFirstTitleOnlyCreated);
		}

		var exists = context.KnownIds.Contains(context.PathId);
		if (!context.FileExists)
		{
			if (exists)
			{
				return new(VaultSyncAction.DeleteFromDatabase, WatcherMessages.Decisions.FileFirstDeletedFileRemovesEntity);
			}

			return new(VaultSyncAction.Ignore, WatcherMessages.Decisions.FileFirstMissingFileUnknownEntity);
		}

		if (context.IssueMessages.Count > 0)
		{
			return new(VaultSyncAction.Conflict, WatcherMessages.Decisions.InvalidCandidateConflict, VaultSyncConcern.MarkdownInvalid);
		}

		if (exists)
		{
			return new(VaultSyncAction.UpdateFromFile, WatcherMessages.Decisions.PathIdentityExists);
		}

		return new(VaultSyncAction.CreateFromFile, WatcherMessages.Decisions.FileFirstFileOriginatedCreation);
	}
}
