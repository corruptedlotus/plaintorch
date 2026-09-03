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
		if (string.IsNullOrWhiteSpace(context.PathId) && IsUntitledPlaceholder(context.PathTitle))
		{
			return new(VaultSyncAction.Ignore, "Untitled placeholder file is ignored until the user finalizes naming and identifier.");
		}

		if (string.IsNullOrWhiteSpace(context.PathId))
		{
			if (context.RequiresCallerInput)
			{
				return new(VaultSyncAction.PurgeFile, "Path identity is missing required caller-provided PUCK input and enforced storage disallows unresolved files.", VaultSyncConcern.PuckViolation);
			}

			return new(VaultSyncAction.PurgeFile, "Title-only file discovered for an auto-generated PUCK entity; enforced storage disallows unresolved files.", VaultSyncConcern.PolicyViolation);
		}

		var exists = context.KnownIds.Contains(context.PathId);
		if (!context.FileExists)
		{
			if (exists)
			{
				return new(VaultSyncAction.RewriteFromDatabase, "Enforced storage keeps canonical entities when files are removed and rewrites canonical markdown.");
			}

			return new(VaultSyncAction.Ignore, "Missing file does not map to a known entity in enforced storage.");
		}

		if (context.IssueMessages.Count > 0)
		{
			if (!exists)
			{
				// An unknown file is disallowed regardless of content, so the policy rejection is the root concern and
				// subsumes the incidental validation issues.
				return new(VaultSyncAction.PurgeFile, "Unknown file with validation issues is disallowed by enforced storage policy.", VaultSyncConcern.PolicyViolation);
			}

			return new(VaultSyncAction.RewriteFromDatabase, "Candidate has validation issues and enforced policy prefers canonical rewrite.", VaultSyncConcern.MarkdownInvalid);
		}

		if (exists)
		{
			return new(VaultSyncAction.UpdateFromFile, "Path identity already exists in the database.");
		}

		return new(VaultSyncAction.PurgeFile, "Unknown file is disallowed by enforced storage policy.", VaultSyncConcern.PolicyViolation);
	}
}
