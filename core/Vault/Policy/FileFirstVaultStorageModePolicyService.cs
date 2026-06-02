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

			return (VaultSyncAction.CreateFromFile, "Title-only file discovered for an auto-generated PUCK entity; file-first storage requires file-originated creation.");
		}

		var exists = context.KnownIds.Contains(context.PathId);
		if (!context.FileExists)
		{
			if (exists)
			{
				return (VaultSyncAction.DeleteFromDatabase, "File-first storage removes known entities when their file is deleted.");
			}

			return (VaultSyncAction.Ignore, "Missing file does not map to a known entity in file-first storage.");
		}

		if (context.IssueMessages.Count > 0)
		{
			return (VaultSyncAction.Conflict, "Candidate has validation issues that require reconciliation.");
		}

		if (exists)
		{
			return (VaultSyncAction.UpdateFromFile, "Path identity already exists in the database.");
		}

		return (VaultSyncAction.CreateFromFile, "File-first storage requires file-originated creation.");
	}
}
