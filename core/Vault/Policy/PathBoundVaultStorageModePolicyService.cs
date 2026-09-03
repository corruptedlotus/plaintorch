using Pleiades.Vault.Watcher;

namespace Pleiades.Vault.Policy;

/// <summary>
/// Provides common path-bound behavior for non-freeform storage modes.
/// </summary>
public abstract class PathBoundVaultStorageModePolicyService : IVaultStorageModePolicyService
{
	/// <inheritdoc />
	public abstract VaultStorageMode Mode { get; }

	/// <inheritdoc />
	// Path-bound modes derive belonging and identity from the path, materialize on create, and have no sync boundary.
	public virtual bool IsIdentityDriven => false;

	/// <inheritdoc />
	public virtual bool MaterializesOnCreate => true;

	/// <inheritdoc />
	public virtual bool BeginsSyncBoundaryOnFirstFile => false;

	/// <inheritdoc />
	public virtual bool PurgesDesyncedFiles => false;

	/// <inheritdoc />
	// Path-bound modes derive identity from the path and are not adopted from an arbitrary user-authored file.
	public virtual bool CanCreateFromFile => false;

	/// <inheritdoc />
	// Path-bound modes always write to the canonical location computed by the storage pipeline.
	public virtual string ResolveWriteTargetPath(object entity, string defaultPath, string? sourcePath) => defaultPath;

	/// <inheritdoc />
	public virtual bool TryResolveWatchPath(VaultPathSyncModel model, string fullPath, bool isDirectoryEvent, out string? inspectPath)
	{
		ArgumentNullException.ThrowIfNull(model);

		if (BelongsToRoots(model, fullPath) && model.IsCandidatePath(fullPath))
		{
			inspectPath = fullPath;
			return true;
		}

		// The self-named-directory fallback below synthesizes a "<dir>/<dir>.md" primary for a directory event. That
		// is only meaningful for a SelfNamedDirectory entity (whose folder *is* its primary file). A SingleFile model
		// (e.g. PolarisCycle under ./Journal) has no such primary, and applying the fallback made a directory event on
		// the entity's own scan root synthesize a bogus "<root>/<root>.md" candidate. Gate it by shape.
		if (!isDirectoryEvent
			|| model.Shape != VaultStorageShape.SelfNamedDirectory
			|| !Directory.Exists(fullPath))
		{
			inspectPath = null;
			return false;
		}

		var directoryName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		if (string.IsNullOrWhiteSpace(directoryName))
		{
			inspectPath = null;
			return false;
		}

		var selfNamedPrimary = Path.Combine(fullPath, $"{directoryName}.md");
		if (BelongsToRoots(model, selfNamedPrimary) && model.IsCandidatePath(selfNamedPrimary))
		{
			inspectPath = selfNamedPrimary;
			return true;
		}

		inspectPath = null;
		return false;
	}

	/// <inheritdoc />
	public virtual Task<bool> BelongsToModelAsync(VaultPathSyncModel model, string fullPath, string markdown, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(model);
		return Task.FromResult(BelongsToRoots(model, fullPath) && model.IsCandidatePath(fullPath));
	}

	/// <inheritdoc />
	public abstract VaultSyncDecision Decide(VaultStorageModeDecisionContext context);

	/// <inheritdoc />
	public virtual string? ResolveRelocationOldIdFallback(VaultPathSyncModel model, string? oldPathId, string? newPathId)
	{
		return oldPathId;
	}

	protected static bool IsUntitledPlaceholder(string pathTitle)
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

	private static bool BelongsToRoots(VaultPathSyncModel model, string fullPath)
	{
		return model.ScanRoots.Any(root => IsPathUnderRoot(fullPath, root));
	}

	private static bool IsPathUnderRoot(string fullPath, string rootPath)
	{
		var normalizedRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		var normalizedPath = Path.GetFullPath(fullPath);
		return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
	}
}
