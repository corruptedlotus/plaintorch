using Pleiades.Puck;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Watcher;

namespace Pleiades.Vault.Policy;

/// <summary>
/// Implements the implicit storage policy. Ownership is identity-driven (frontmatter PUCK) like freeform storage, but an
/// implicit entity only becomes deletion-authoritative once its synchronization boundary has begun.
/// </summary>
/// <remarks>
/// Path resolution stays path-bound (inherited from <see cref="PathBoundVaultStorageModePolicyService"/>) so implicit
/// entities are still classified by their configured scan roots and candidate shape rather than claiming every markdown
/// file in the vault. Identity is layered on top of the path-bound belonging check.
/// </remarks>
public sealed class ImplicitVaultStorageModePolicyService(
	MarkdownFrontMatterSerializer markdownSerializer,
	PuckEntityResolutionService puckEntityResolutionService) : PathBoundVaultStorageModePolicyService
{
	/// <inheritdoc />
	public override VaultStorageMode Mode => VaultStorageMode.Implicit;

	/// <inheritdoc />
	// Implicit belonging is by frontmatter identity, it stays database-first (no file) until its boundary is begun,
	// and a first-appearing file begins that boundary — after which deletion of the file is authoritative.
	public override bool IsIdentityDriven => true;

	/// <inheritdoc />
	public override bool MaterializesOnCreate => false;

	/// <inheritdoc />
	public override bool BeginsSyncBoundaryOnFirstFile => true;

	/// <inheritdoc />
	public override async Task<bool> BelongsToModelAsync(VaultPathSyncModel model, string fullPath, string markdown, CancellationToken cancellationToken)
	{
		if (!await base.BelongsToModelAsync(model, fullPath, markdown, cancellationToken))
		{
			return false;
		}

		// A missing file (deletion or not-yet-materialized boundary) still flows through so deletion authority can apply.
		if (!File.Exists(fullPath))
		{
			return true;
		}

		var frontMatter = markdownSerializer.ParseFrontMatter(markdown);
		if (!frontMatter.TryGetValue("puck", out var rawPuck) || string.IsNullOrWhiteSpace(rawPuck))
		{
			return false;
		}

		var puck = rawPuck.Trim().Trim('"');
		if (string.IsNullOrWhiteSpace(puck))
		{
			return false;
		}

		var resolved = await puckEntityResolutionService.ResolveAsync(puck, cancellationToken);
		if (resolved.Exists)
		{
			return string.Equals(resolved.EntityType, model.EntityName, StringComparison.Ordinal);
		}

		return true;
	}

	/// <inheritdoc />
	public override VaultSyncDecision Decide(VaultStorageModeDecisionContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (string.IsNullOrWhiteSpace(context.PathId))
		{
			return new(VaultSyncAction.Ignore, "Implicit storage does not auto-create entities from files without frontmatter PUCK identity.");
		}

		var exists = context.KnownIds.Contains(context.PathId);
		if (!context.FileExists)
		{
			if (exists && context.BoundaryBegun)
			{
				return new(VaultSyncAction.DeleteFromDatabase, "Implicit storage removes known entities when their boundary-begun file is deleted.");
			}

			if (exists)
			{
				return new(VaultSyncAction.Ignore, "Implicit entity has no begun synchronization boundary, so a missing file is not authoritative.");
			}

			return new(VaultSyncAction.Ignore, "Missing implicit file does not map to a known PUCK identity.");
		}

		if (context.IssueMessages.Count > 0)
		{
			if (!exists)
			{
				// The unknown/unresolvable frontmatter PUCK is the root concern; the validation issues are subsumed.
				return new(VaultSyncAction.PurgeFile, "Unknown implicit PUCK assertion with validation issues is disallowed by implicit policy.", VaultSyncConcern.PuckViolation);
			}

			return new(VaultSyncAction.RewriteFromDatabase, "Implicit candidate has validation issues and must be rewritten from canonical state.", VaultSyncConcern.MarkdownInvalid);
		}

		if (exists)
		{
			return new(VaultSyncAction.UpdateFromFile, "Frontmatter PUCK identity exists in storage and can be synced from file.");
		}

		return new(VaultSyncAction.PurgeFile, "Implicit storage rejects unknown frontmatter PUCK assertions.", VaultSyncConcern.PuckViolation);
	}
}
