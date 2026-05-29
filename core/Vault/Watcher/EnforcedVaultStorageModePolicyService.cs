namespace Pleiades.Vault.Watcher;

/// <summary>
/// Implements enforced storage mode policy.
/// </summary>
public sealed class EnforcedVaultStorageModePolicyService : PathBoundVaultStorageModePolicyService
{
	/// <inheritdoc />
	public override VaultStorageMode Mode => VaultStorageMode.Enforced;

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
				return (VaultSyncAction.PurgeFile, "Path identity is missing required caller-provided PUCK input and enforced storage disallows unresolved files.");
			}

			return (VaultSyncAction.CreateFromFile, "Title-only file discovered for an auto-generated PUCK entity; enforced storage allows canonical create-from-file.");
		}

		var exists = context.KnownIds.Contains(context.PathId);
		if (context.IssueMessages.Count > 0)
		{
			if (!exists)
			{
				return (VaultSyncAction.PurgeFile, "Unknown file with validation issues is disallowed by enforced storage policy.");
			}

			return (VaultSyncAction.RewriteFromDatabase, "Candidate has validation issues and enforced policy prefers canonical rewrite.");
		}

		if (exists)
		{
			return (VaultSyncAction.UpdateFromFile, "Path identity already exists in the database.");
		}

		return (VaultSyncAction.PurgeFile, "Unknown file is disallowed by enforced storage policy.");
	}
}
