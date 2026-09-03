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
			return new(VaultSyncAction.Ignore, "Untitled placeholder file is ignored until the user finalizes naming and identifier.");
		}

		if (string.IsNullOrWhiteSpace(context.PathId))
		{
			if (context.RequiresCallerInput)
			{
				return new(VaultSyncAction.Ignore, "Path identity is missing required caller-provided PUCK input and optional storage cannot create entities from this file.");
			}

			return new(VaultSyncAction.Ignore, "Optional storage does not create new title-only files by default.");
		}

		var exists = context.KnownIds.Contains(context.PathId);
		if (!context.FileExists)
		{
			if (exists)
			{
				return new(VaultSyncAction.DeleteFromDatabase, "Optional storage removes known entities when their file is deleted.");
			}

			return new(VaultSyncAction.Ignore, "Missing file does not map to a known entity in optional storage.");
		}

		if (context.IssueMessages.Count > 0)
		{
			if (!exists)
			{
				// Optional storage passively ignores unknown standalone files; a validation issue on such a file is still
				// surfaced by the reporter's markdown floor, but the mode itself asserts no policy/identity concern here.
				return new(VaultSyncAction.Ignore, "Optional storage does not create new entities from invalid standalone files.");
			}

			return new(VaultSyncAction.RewriteFromDatabase, "Candidate has validation issues and optional policy prefers canonical rewrite.", VaultSyncConcern.MarkdownInvalid);
		}

		if (exists)
		{
			return new(VaultSyncAction.UpdateFromFile, "Path identity already exists in the database.");
		}

		return new(VaultSyncAction.Ignore, "Optional storage does not create new entities from standalone files by default.");
	}
}
