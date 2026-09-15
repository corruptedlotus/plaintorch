using Pleiades.Resources;
using Pleiades.Vault.Watcher;

namespace Pleiades.Vault.Policy;

/// <summary>
/// Implements optional storage mode policy.
/// </summary>
public sealed class OptionalVaultStorageModePolicyService : PathBoundVaultStorageModePolicyService
{
	/// <inheritdoc />
	public override VaultStorageMode Mode => VaultStorageMode.Optional;

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
				return new(VaultSyncAction.Ignore, WatcherMessages.Decisions.OptionalMissingRequiredPuckInputIgnored);
			}

			return new(VaultSyncAction.Ignore, WatcherMessages.Decisions.OptionalTitleOnlyIgnored);
		}

		var exists = context.KnownIds.Contains(context.PathId);
		if (!context.FileExists)
		{
			if (exists)
			{
				return new(VaultSyncAction.DeleteFromDatabase, WatcherMessages.Decisions.OptionalDeletedFileRemovesEntity);
			}

			return new(VaultSyncAction.Ignore, WatcherMessages.Decisions.OptionalMissingFileUnknownEntity);
		}

		if (context.IssueMessages.Count > 0)
		{
			if (!exists)
			{
				// Optional storage passively ignores unknown standalone files; a validation issue on such a file is still
				// surfaced by the reporter's markdown floor, but the mode itself asserts no policy/identity concern here.
				return new(VaultSyncAction.Ignore, WatcherMessages.Decisions.OptionalInvalidStandaloneIgnored);
			}

			return new(VaultSyncAction.RewriteFromDatabase, WatcherMessages.Decisions.OptionalInvalidCandidateRewritten, VaultSyncConcern.MarkdownInvalid);
		}

		if (exists)
		{
			return new(VaultSyncAction.UpdateFromFile, WatcherMessages.Decisions.PathIdentityExists);
		}

		return new(VaultSyncAction.Ignore, WatcherMessages.Decisions.OptionalStandaloneIgnored);
	}
}
