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
	public override (VaultSyncAction Action, string Reason) Decide(VaultStorageModeDecisionContext context)
	{
		if (string.IsNullOrWhiteSpace(context.PathId) && IsUntitledPlaceholder(context.PathTitle))
		{
			return (VaultSyncAction.Ignore, "Untitled placeholder file is ignored until the user finalizes naming and identifier.");
		}

		if (string.IsNullOrWhiteSpace(context.PathId))
		{
			if (context.RequiresCallerInput)
			{
				return (VaultSyncAction.Conflict, "Path identity is missing required caller-provided PUCK input.");
			}

			return (VaultSyncAction.CreateFromFile, "Title-only file discovered for an auto-generated PUCK entity; synced storage allows file-originated creation.");
		}

		var exists = context.KnownIds.Contains(context.PathId);
		if (!context.FileExists)
		{
			if (exists)
			{
				return (VaultSyncAction.DeleteFromDatabase, "Synced storage removes known entities when their file is deleted.");
			}

			return (VaultSyncAction.Ignore, "Missing file does not map to a known entity in synced storage.");
		}

		if (context.IssueMessages.Count > 0)
		{
			if (!exists)
			{
				return (VaultSyncAction.Conflict, "Candidate has validation issues that require reconciliation.");
			}

			return (VaultSyncAction.RewriteFromDatabase, "Candidate has validation issues and synced policy prefers canonical rewrite.");
		}

		if (exists)
		{
			return (VaultSyncAction.UpdateFromFile, "Path identity already exists in the database.");
		}

		return (VaultSyncAction.CreateFromFile, "Synced storage allows file-originated creation.");
	}
}
