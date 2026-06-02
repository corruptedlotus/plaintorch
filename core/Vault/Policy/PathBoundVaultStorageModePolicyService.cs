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
	public virtual bool TryResolveWatchPath(VaultPathSyncModel model, string fullPath, bool isDirectoryEvent, out string? inspectPath)
	{
		ArgumentNullException.ThrowIfNull(model);

		if (BelongsToRoots(model, fullPath) && model.IsCandidatePath(fullPath))
		{
			inspectPath = fullPath;
			return true;
		}

		if (!isDirectoryEvent || !Directory.Exists(fullPath))
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
	public abstract (VaultSyncAction Action, string Reason) Decide(VaultStorageModeDecisionContext context);

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
