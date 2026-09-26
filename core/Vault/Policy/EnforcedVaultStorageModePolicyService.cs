using Pleiades.Resources;
using Pleiades.Vault.Watcher;

namespace Pleiades.Vault.Policy;

/// <summary>
/// Implements enforced storage mode policy.
/// </summary>
public sealed class EnforcedVaultStorageModePolicyService : PathBoundVaultStorageModePolicyService
{
	/// <inheritdoc />
	public override VaultStorageMode Mode => VaultStorageMode.Enforced;

	/// <inheritdoc />
	// Enforced keeps its root a clean outward interface: files that no longer map to a live entity are purged.
	public override bool PurgesDesyncedFiles => true;

	/// <inheritdoc />
	public override VaultSyncDecision Decide(VaultStorageModeDecisionContext context)
	{
		// A missing file has no content to judge and, without an identity, no entity to reconcile — a note deleted before
		// it was ever synced, or the primary a folder event synthesizes — so there is nothing to create, purge or hold in
		// conflict.
		if (!context.FileExists && string.IsNullOrWhiteSpace(context.PathId))
		{
			return new(VaultSyncAction.Ignore, WatcherMessages.Decisions.EnforcedMissingFileUnknownEntity);
		}

		if (string.IsNullOrWhiteSpace(context.PathId) && IsUntitledPlaceholder(context.PathTitle))
		{
			return new(VaultSyncAction.Ignore, WatcherMessages.Decisions.UntitledPlaceholderIgnored);
		}

		if (string.IsNullOrWhiteSpace(context.PathId))
		{
			if (context.RequiresCallerInput)
			{
				return new(VaultSyncAction.PurgeFile, WatcherMessages.Decisions.EnforcedMissingRequiredPuckInputPurged, VaultSyncConcern.PuckViolation);
			}

			return new(VaultSyncAction.PurgeFile, WatcherMessages.Decisions.EnforcedTitleOnlyPurged, VaultSyncConcern.PolicyViolation);
		}

		var exists = context.KnownIds.Contains(context.PathId);
		if (!context.FileExists)
		{
			if (exists)
			{
				return new(VaultSyncAction.RewriteFromDatabase, WatcherMessages.Decisions.EnforcedDeletedFileRewritten);
			}

			return new(VaultSyncAction.Ignore, WatcherMessages.Decisions.EnforcedMissingFileUnknownEntity);
		}

		if (context.IssueMessages.Count > 0)
		{
			if (!exists)
			{
				// An unknown file is disallowed regardless of content, so the policy rejection is the root concern and
				// subsumes the incidental validation issues.
				return new(VaultSyncAction.PurgeFile, WatcherMessages.Decisions.EnforcedUnknownFileWithIssuesPurged, VaultSyncConcern.PolicyViolation);
			}

			return new(VaultSyncAction.RewriteFromDatabase, WatcherMessages.Decisions.EnforcedInvalidCandidateRewritten, VaultSyncConcern.MarkdownInvalid);
		}

		if (exists)
		{
			return new(VaultSyncAction.UpdateFromFile, WatcherMessages.Decisions.PathIdentityExists);
		}

		return new(VaultSyncAction.PurgeFile, WatcherMessages.Decisions.EnforcedUnknownFilePurged, VaultSyncConcern.PolicyViolation);
	}
}
