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
	/// <param name="model">The path sync model classifying the candidate.</param>
	/// <param name="pathId">The resolved candidate identifier, when available.</param>
	/// <param name="pathTitle">The resolved candidate title parsed from path/frontmatter.</param>
	/// <param name="issueMessages">Validation issue messages produced while hydrating the candidate.</param>
	/// <param name="knownIds">The known identifiers currently present in storage for the model type.</param>
	/// <returns>A provisional action and explanatory reason for watcher reconciliation.</returns>
	public (VaultSyncAction Action, string Reason) Decide(VaultPathSyncModel model, string? pathId, string pathTitle, IReadOnlyList<string> issueMessages, ISet<string> knownIds)
	{
		ArgumentNullException.ThrowIfNull(model);
		ArgumentException.ThrowIfNullOrWhiteSpace(pathTitle);
		ArgumentNullException.ThrowIfNull(issueMessages);
		ArgumentNullException.ThrowIfNull(knownIds);

		if (string.IsNullOrWhiteSpace(pathId) && IsUntitledPlaceholder(pathTitle))
		{
			return (VaultSyncAction.Ignore, "Untitled placeholder file is ignored until the user finalizes naming and identifier.");
		}

		var requiresCallerInput = puckCreationService.RequiresCallerInputFor(model.EntityType);
		if (string.IsNullOrWhiteSpace(pathId))
		{
			if (model.Mode == VaultStorageMode.Freeform)
			{
				return (VaultSyncAction.Ignore, "Freeform storage does not auto-create entities from files without frontmatter PUCK identity.");
			}

			if (requiresCallerInput)
			{
				return model.Mode switch
				{
					VaultStorageMode.Enforced => (VaultSyncAction.PurgeFile, "Path identity is missing required caller-provided PUCK input and enforced storage disallows unresolved files."),
					VaultStorageMode.Optional => (VaultSyncAction.Ignore, "Path identity is missing required caller-provided PUCK input and optional storage cannot create entities from this file."),
					VaultStorageMode.Synced => (VaultSyncAction.Conflict, "Path identity is missing required caller-provided PUCK input."),
					VaultStorageMode.FileFirst => (VaultSyncAction.Conflict, "Path identity is missing required caller-provided PUCK input."),
					_ => (VaultSyncAction.Conflict, "Path identity is missing required caller-provided PUCK input."),
				};
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
			if (!exists)
			{
				return model.Mode switch
				{
					VaultStorageMode.Enforced => (VaultSyncAction.PurgeFile, "Unknown file with validation issues is disallowed by enforced storage policy."),
					VaultStorageMode.Optional => (VaultSyncAction.Ignore, "Optional storage does not create new entities from invalid standalone files."),
					_ => (VaultSyncAction.Conflict, "Candidate has validation issues that require reconciliation."),
				};
			}

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
			VaultStorageMode.Freeform => (VaultSyncAction.PurgeFile, "Freeform storage rejects unknown frontmatter PUCK assertions."),
			VaultStorageMode.Enforced => (VaultSyncAction.PurgeFile, "Unknown file is disallowed by enforced storage policy."),
			VaultStorageMode.Optional => (VaultSyncAction.Ignore, "Optional storage does not create new entities from standalone files by default."),
			VaultStorageMode.Synced => (VaultSyncAction.CreateFromFile, "Synced storage allows file-originated creation."),
			VaultStorageMode.FileFirst => (VaultSyncAction.CreateFromFile, "File-first storage requires file-originated creation."),
			_ => (VaultSyncAction.Conflict, "No storage policy matched the discovered file."),
		};
	}

	private static bool IsUntitledPlaceholder(string pathTitle)
	{
		var trimmed = pathTitle.Trim();
		if (string.Equals(trimmed, "Untitled", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		if (!trimmed.StartsWith("Untitled ", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		return int.TryParse(trimmed["Untitled ".Length..], out _);
	}
}
