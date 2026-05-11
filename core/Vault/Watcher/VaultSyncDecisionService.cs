using Pleiades.Puck;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Produces provisional watcher reconciliation decisions from path-resolved markdown candidates.
/// </summary>
public sealed class VaultSyncDecisionService(PuckCreationService puckCreationService)
{
	/// <summary>
	/// Evaluates a discovered candidate against known entity identifiers and storage policy.
	/// </summary>
	public (VaultSyncAction Action, string Reason) Decide(VaultPathSyncModel model, string? pathId, IReadOnlyList<string> issueMessages, ISet<string> knownIds)
	{
		ArgumentNullException.ThrowIfNull(model);
		ArgumentNullException.ThrowIfNull(issueMessages);
		ArgumentNullException.ThrowIfNull(knownIds);

		var requiresCallerInput = puckCreationService.RequiresCallerInputFor(model.EntityType);
		if (string.IsNullOrWhiteSpace(pathId))
		{
			if (requiresCallerInput)
			{
				return (VaultSyncAction.Conflict, "Path identity is missing required caller-provided PUCK input.");
			}

			return model.Mode switch
			{
				VaultStorageMode.Optional => (VaultSyncAction.Ignore, "Optional storage does not create new title-only files by default."),
				VaultStorageMode.Enforced => (VaultSyncAction.CreateFromFile, "Title-only file discovered for an auto-generated PUCK entity; the core should issue a new identifier."),
				VaultStorageMode.Synced => (VaultSyncAction.CreateFromFile, "Title-only file discovered for an auto-generated PUCK entity; synced storage allows file-originated creation."),
				VaultStorageMode.FileFirst => (VaultSyncAction.CreateFromFile, "Title-only file discovered for an auto-generated PUCK entity; file-first storage requires file-originated creation."),
				_ => (VaultSyncAction.Conflict, "Path identity is missing a PUCK identifier."),
			};
		}

		var exists = knownIds.Contains(pathId);
		if (issueMessages.Count > 0)
		{
			return exists && model.Mode != VaultStorageMode.FileFirst
				? (VaultSyncAction.RewriteFromDatabase, "Candidate has validation issues and policy prefers canonical rewrite.")
				: (VaultSyncAction.Conflict, "Candidate has validation issues that require reconciliation.");
		}

		if (exists)
		{
			return (VaultSyncAction.UpdateFromFile, "Path identity already exists in the database.");
		}

		return model.Mode switch
		{
			VaultStorageMode.Enforced => (VaultSyncAction.PurgeFile, "Unknown file is disallowed by enforced storage policy."),
			VaultStorageMode.Optional => (VaultSyncAction.Ignore, "Optional storage does not create new entities from standalone files by default."),
			VaultStorageMode.Synced => (VaultSyncAction.CreateFromFile, "Synced storage allows file-originated creation."),
			VaultStorageMode.FileFirst => (VaultSyncAction.CreateFromFile, "File-first storage requires file-originated creation."),
			_ => (VaultSyncAction.Conflict, "No storage policy matched the discovered file."),
		};
	}
}
