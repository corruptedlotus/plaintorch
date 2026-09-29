using Pleiades.Puck;
using Pleiades.Resources;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Watcher;

namespace Pleiades.Vault.Policy;

/// <summary>
/// Implements the implicit storage policy. Ownership is identity-driven (frontmatter PUCK) like freeform storage, but an
/// implicit entity only becomes deletion-authoritative once its synchronization boundary has begun.
/// </summary>
/// <remarks>
/// Watch-path resolution stays path-bound (inherited from <see cref="PathBoundVaultStorageModePolicyService"/>): the
/// configured scan roots and candidate shape only <em>propose</em> the kind for a path, so an implicit model does not claim
/// every markdown file in the vault. Belonging is decided by identity — an existing note is this kind's when the PUCK it
/// asserts is — and discovery settles a note's kind by what it asserts before asking, so a note is never lost to a
/// path shape that proposed the wrong kind.
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
	// An implicit entity is normally database-first, but init adopts an existing user-authored file into a new entity.
	public override bool CanCreateFromFile => true;

	/// <inheritdoc />
	public override string ResolveWriteTargetPath(object entity, string defaultPath, string? sourcePath, string? existingPath)
	{
		// The canonical path (its partition under the hosting directive) is only the default for a brand-new
		// materialisation. An implicit note the user authored anywhere valid inside its parent is kept where it is —
		// the partition is the default location for creation, not an authoritative one detection relocates into. Only
		// the file name tracks the canonical (title-derived) base name, so a rename still lands beside the original.
		var anchor = FirstExistingFile(sourcePath, existingPath);
		if (anchor is null)
		{
			return defaultPath;
		}

		var directory = Path.GetDirectoryName(anchor);
		return string.IsNullOrWhiteSpace(directory)
			? defaultPath
			: Path.Combine(directory, Path.GetFileName(defaultPath));
	}

	private static string? FirstExistingFile(params string?[] candidatePaths)
	{
		foreach (var candidatePath in candidatePaths)
		{
			if (string.IsNullOrWhiteSpace(candidatePath))
			{
				continue;
			}

			var fullPath = Path.GetFullPath(candidatePath);
			if (File.Exists(fullPath))
			{
				return fullPath;
			}
		}

		return null;
	}

	/// <inheritdoc />
	// A missing file (a deletion, or a boundary not yet materialized) has no identity left to read, so only its path can
	// place it, and it still flows through so deletion authority can apply. An existing note belongs by the identity it
	// asserts, wherever it sits: whether it may assert it there is the assertion territory's question, flagged by
	// discovery rather than answered here by silently dropping the note.
	public override async Task<bool> BelongsToModelAsync(VaultPathSyncModel model, string fullPath, string markdown, CancellationToken cancellationToken)
	{
		if (!File.Exists(fullPath))
		{
			return await base.BelongsToModelAsync(model, fullPath, markdown, cancellationToken);
		}

		if (!string.Equals(Path.GetExtension(fullPath), ".md", StringComparison.OrdinalIgnoreCase))
		{
			return false;
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
			return new(VaultSyncAction.Ignore, WatcherMessages.Decisions.ImplicitNoIdentityIgnored);
		}

		var exists = context.KnownIds.Contains(context.PathId);
		if (!context.FileExists)
		{
			if (exists && context.BoundaryBegun)
			{
				return new(VaultSyncAction.DeleteFromDatabase, WatcherMessages.Decisions.ImplicitDeletedFileRemovesEntity);
			}

			if (exists)
			{
				return new(VaultSyncAction.Ignore, WatcherMessages.Decisions.ImplicitBoundaryNotBegun);
			}

			return new(VaultSyncAction.Ignore, WatcherMessages.Decisions.ImplicitMissingFileUnknownIdentity);
		}

		if (context.IssueMessages.Count > 0)
		{
			if (!exists)
			{
				// Implicit belonging is identity-driven in a non-exclusive root: the core does not destroy the file, but
				// asserting a PUCK it does not recognise is illegal — left in place, flagged as an error for the user to
				// resolve (dismissible), not silently purged.
				return new(VaultSyncAction.Ignore, WatcherMessages.Decisions.ImplicitUnrecognisedAssertionWithIssues, VaultSyncConcern.ForeignFile);
			}

			return new(VaultSyncAction.RewriteFromDatabase, WatcherMessages.Decisions.ImplicitInvalidCandidateRewritten, VaultSyncConcern.MarkdownInvalid);
		}

		if (exists)
		{
			return new(VaultSyncAction.UpdateFromFile, WatcherMessages.Decisions.FrontmatterIdentityExists);
		}

		return new(VaultSyncAction.Ignore, WatcherMessages.Decisions.ImplicitUnrecognisedAssertion, VaultSyncConcern.ForeignFile);
	}
}
