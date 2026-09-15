using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Resources;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Watcher;

namespace Pleiades.Vault.Policy;

/// <summary>
/// Implements freeform storage policy where ownership is identity-driven (frontmatter PUCK) rather than path-shape matching.
/// </summary>
public sealed class FreeformVaultStorageModePolicyService(
	VaultLayout layout,
	MarkdownFrontMatterSerializer markdownSerializer,
	PuckEntityResolutionService puckEntityResolutionService,
	VaultEntityModelCatalog entityModelCatalog) : IVaultStorageModePolicyService
{
	/// <inheritdoc />
	public VaultStorageMode Mode => VaultStorageMode.Freeform;

	/// <inheritdoc />
	// Freeform belonging is by frontmatter identity; a directive's note may live anywhere and materializes on create.
	public bool IsIdentityDriven => true;

	/// <inheritdoc />
	public bool MaterializesOnCreate => true;

	/// <inheritdoc />
	public bool BeginsSyncBoundaryOnFirstFile => false;

	/// <inheritdoc />
	public bool PurgesDesyncedFiles => false;

	/// <inheritdoc />
	// A freeform entity is adopted from a user-authored file anywhere in the vault, minting an identity when it has none.
	public bool CanCreateFromFile => true;

	/// <inheritdoc />
	public bool TryResolveWatchPath(VaultPathSyncModel model, string fullPath, bool isDirectoryEvent, out string? inspectPath)
	{
		if (isDirectoryEvent || !string.Equals(Path.GetExtension(fullPath), ".md", StringComparison.OrdinalIgnoreCase))
		{
			inspectPath = null;
			return false;
		}

		var normalizedRoot = Path.GetFullPath(layout.VaultRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		if (!Path.GetFullPath(fullPath).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
		{
			inspectPath = null;
			return false;
		}

		inspectPath = fullPath;
		return true;
	}

	/// <inheritdoc />
	public async Task<bool> BelongsToModelAsync(VaultPathSyncModel model, string fullPath, string markdown, CancellationToken cancellationToken)
	{
		if (!string.Equals(Path.GetExtension(fullPath), ".md", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

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
			// A polymorphic family (Directive → Stellar/Lunar) resolves to its CONCRETE member, so an exact name match
			// against the abstract family model would reject every directive file — the watcher would ignore directive
			// files entirely, never adopting an edit and never flagging a duplicate. Accept a resolved concrete type
			// that is a member of the model's family (the family check subsumes the anchor-name match); this is the
			// read-side twin of the family-aware write-target resolution.
			return entityModelCatalog.IsFamilyMember(model.EntityType, resolved.EntityType);
		}

		return true;
	}

	/// <inheritdoc />
	public VaultSyncDecision Decide(VaultStorageModeDecisionContext context)
	{
		if (string.IsNullOrWhiteSpace(context.PathId))
		{
			return new(VaultSyncAction.Ignore, WatcherMessages.Decisions.FreeformNoIdentityIgnored);
		}

		var exists = context.KnownIds.Contains(context.PathId);
		if (!context.FileExists)
		{
			if (exists)
			{
				return new(VaultSyncAction.DeleteFromDatabase, WatcherMessages.Decisions.FreeformDeletedFileRemovesEntity);
			}

			return new(VaultSyncAction.Ignore, WatcherMessages.Decisions.FreeformMissingFileUnknownIdentity);
		}

		if (context.IssueMessages.Count > 0)
		{
			if (!exists)
			{
				// Freeform is a non-exclusive root: the core does not destroy the file, but asserting a PUCK it does not
				// recognise is illegal — left in place, flagged as an error for the user to resolve (dismissible), not
				// purged.
				return new(VaultSyncAction.Ignore, WatcherMessages.Decisions.FreeformUnrecognisedAssertionWithIssues, VaultSyncConcern.ForeignFile);
			}

			return new(VaultSyncAction.RewriteFromDatabase, WatcherMessages.Decisions.FreeformInvalidCandidateRewritten, VaultSyncConcern.MarkdownInvalid);
		}

		if (exists)
		{
			return new(VaultSyncAction.UpdateFromFile, WatcherMessages.Decisions.FrontmatterIdentityExists);
		}

		return new(VaultSyncAction.Ignore, WatcherMessages.Decisions.FreeformUnrecognisedAssertion, VaultSyncConcern.ForeignFile);
	}

	/// <inheritdoc />
	public string? ResolveRelocationOldIdFallback(VaultPathSyncModel model, string? oldPathId, string? newPathId)
	{
		if (!string.IsNullOrWhiteSpace(oldPathId))
		{
			return oldPathId;
		}

		return model.EntityType == typeof(Directive)
			? newPathId
			: oldPathId;
	}

	/// <inheritdoc />
	public string ResolveWriteTargetPath(object entity, string defaultPath, string? sourcePath, string? existingPath)
	{
		ArgumentNullException.ThrowIfNull(entity);
		var anchor = !string.IsNullOrWhiteSpace(sourcePath) ? sourcePath : existingPath;
		if (string.IsNullOrWhiteSpace(anchor))
		{
			return defaultPath;
		}

		var fullAnchor = Path.GetFullPath(anchor);
		if (!File.Exists(fullAnchor))
		{
			return defaultPath;
		}

		// A freeform file the user authored anywhere is kept at its authored location, unless that location is another
		// entity's managed root — then it falls back to a free canonical slot rather than intruding on managed space.
		return IsAllowedFreeformAssertion(entity.GetType(), fullAnchor)
			? fullAnchor
			: ResolveFreeformFallbackPath(defaultPath);
	}

	private bool IsAllowedFreeformAssertion(Type entityType, string fullSourcePath)
	{
		var sourceDirectory = Path.GetDirectoryName(fullSourcePath);
		if (string.IsNullOrWhiteSpace(sourceDirectory))
		{
			return false;
		}

		if (IsUnderNonDirectiveManagedRoot(sourceDirectory))
		{
			return false;
		}

		if (entityType == typeof(Directive))
		{
			var folder = Path.GetDirectoryName(fullSourcePath)!;
			var parentDirectory = Directory.GetParent(folder)?.FullName;
			if (!string.IsNullOrWhiteSpace(parentDirectory) && IsUnderNonDirectiveManagedRoot(parentDirectory))
			{
				return false;
			}
		}

		return true;
	}

	private bool IsUnderNonDirectiveManagedRoot(string path)
	{
		var fullPath = Path.GetFullPath(path);
		return IsUnderRoot(fullPath, layout.MetadataRoot)
			|| IsUnderRoot(fullPath, layout.ObjectivesRoot)
			|| IsUnderRoot(fullPath, layout.FatesRoot)
			|| IsUnderRoot(fullPath, layout.DecreesRoot)
			|| IsUnderRoot(fullPath, layout.OnrushRoot)
			|| IsUnderRoot(fullPath, layout.JournalRoot)
			|| IsUnderRoot(fullPath, layout.SagaRoot);
	}

	private static bool IsUnderRoot(string fullPath, string root)
	{
		var normalizedPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase)
			|| normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	}

	private static string ResolveFreeformFallbackPath(string defaultPath)
	{
		var directory = Path.GetDirectoryName(defaultPath)
			?? throw new InvalidOperationException("Freeform fallback path does not have a directory.");
		var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(defaultPath);
		var extension = Path.GetExtension(defaultPath);
		var fallbackDirectory = directory;
		var fallbackPath = defaultPath;
		var suffix = 2;

		while (File.Exists(fallbackPath) || Directory.Exists(fallbackDirectory))
		{
			fallbackDirectory = Path.Combine(Path.GetDirectoryName(directory) ?? directory, $"{Path.GetFileName(directory)} ({suffix})");
			var fallbackFileName = $"{fileNameWithoutExtension} ({suffix})";
			fallbackPath = Path.Combine(fallbackDirectory, $"{fallbackFileName}{extension}");
			suffix++;
		}

		return fallbackPath;
	}
}
