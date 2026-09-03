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
		if (string.IsNullOrWhiteSpace(context.PathId) && IsUntitledPlaceholder(context.PathTitle))
		{
			return new(VaultSyncAction.Ignore, "Untitled placeholder file is ignored until the user finalizes naming and identifier.");
		}

		if (string.IsNullOrWhiteSpace(context.PathId))
		{
			if (context.RequiresCallerInput)
			{
				return new(VaultSyncAction.Conflict, "Path identity is missing required caller-provided PUCK input.", VaultSyncConcern.PuckViolation);
			}

			return new(VaultSyncAction.CreateFromFile, "Title-only file discovered for an auto-generated PUCK entity; synced storage allows file-originated creation.");
		}

		var exists = context.KnownIds.Contains(context.PathId);
		if (!context.FileExists)
		{
			if (exists)
			{
				return new(VaultSyncAction.DeleteFromDatabase, "Synced storage removes known entities when their file is deleted.");
			}

			return new(VaultSyncAction.Ignore, "Missing file does not map to a known entity in synced storage.");
		}

		if (context.IssueMessages.Count > 0)
		{
			if (!exists)
			{
				return new(VaultSyncAction.Conflict, "Candidate has validation issues that require reconciliation.", VaultSyncConcern.MarkdownInvalid);
			}

			return new(VaultSyncAction.RewriteFromDatabase, "Candidate has validation issues and synced policy prefers canonical rewrite.", VaultSyncConcern.MarkdownInvalid);
		}

		if (exists)
		{
			return new(VaultSyncAction.UpdateFromFile, "Path identity already exists in the database.");
		}

		return new(VaultSyncAction.CreateFromFile, "Synced storage allows file-originated creation.");
	}
}
