using System.Text.Json;
using System.Reflection;
using Pleiades.Vault.Markdown;

using Pleiades.Vault.Watcher;

namespace Pleiades.Vault.Policy;

/// <summary>
/// Centralizes watcher path filtering and scan-root selection rules defined by the watcher blueprint.
/// </summary>
public sealed class VaultWatcherPathPolicy(VaultLayout layout)
{
	private const string ObsidianConfigRelativePath = ".obsidian/app.json";
	private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
	private static readonly HashSet<string> PartitionFolderNames = ResolvePartitionFolderNames();

	/// <summary>
	/// Returns distinct watcher roots, optionally including full-vault observation when any model is freeform.
	/// </summary>
	public IReadOnlyList<string> GetWatchRoots(VaultPathSyncModelCatalog modelCatalog)
	{
		ArgumentNullException.ThrowIfNull(modelCatalog);

		var roots = modelCatalog
			.GetScanRoots()
			.Select(Path.GetFullPath)
			.ToHashSet(PathComparer);

		if (modelCatalog.GetModels().Any(static model => model.Mode.IsIdentityDriven()))
		{
			roots.Add(Path.GetFullPath(layout.VaultRoot));
		}

		return roots
			.OrderByDescending(static root => root.Length)
			.ToArray();
	}

	/// <summary>
	/// Determines whether the vault is structurally reachable: the vault root and every entity root that exists can be
	/// enumerated. A failure here is a whole-of-vault (tier-2) condition — an unmounted drive, a revoked permission on a
	/// root — that the watcher answers by going to sleep and periodically re-probing, rather than by retrying a single
	/// file. A root that simply does not exist yet (never created) is not a failure; only one that exists but cannot be
	/// accessed is. When it returns <see langword="false"/>, <paramref name="inaccessiblePath"/> names the offending root.
	/// </summary>
	public bool IsVaultStructurallyAccessible(out string? inaccessiblePath)
	{
		inaccessiblePath = null;
		var vaultRoot = Path.GetFullPath(layout.VaultRoot);
		if (!Directory.Exists(vaultRoot) || !CanEnumerate(vaultRoot))
		{
			inaccessiblePath = vaultRoot;
			return false;
		}

		foreach (var root in EntityRoots())
		{
			var fullRoot = Path.GetFullPath(root);
			if (Directory.Exists(fullRoot) && !CanEnumerate(fullRoot))
			{
				inaccessiblePath = fullRoot;
				return false;
			}
		}

		return true;
	}

	private static bool CanEnumerate(string directory)
	{
		try
		{
			// Directory.Exists hides permission/IO failures behind a bare false; force an actual read of the first entry
			// so a revoked-access or unmounted root surfaces as the structural failure it is.
			using var enumerator = Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
			enumerator.MoveNext();
			return true;
		}
		catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
		{
			return false;
		}
	}

	private IEnumerable<string> EntityRoots()
	{
		yield return layout.DirectivesRoot;
		yield return layout.ObjectivesRoot;
		yield return layout.FatesRoot;
		yield return layout.DecreesRoot;
		yield return layout.OnrushRoot;
		yield return layout.JournalRoot;
		yield return layout.SagaRoot;
		yield return layout.MetadataRoot;
	}

	/// <summary>
	/// Determines whether the watcher should ignore a path for scan and live-event processing.
	/// </summary>
	public bool ShouldIgnorePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return true;
		}

		var fullPath = Path.GetFullPath(path);
		if (!IsUnderVaultRoot(fullPath))
		{
			return true;
		}

		if (IsUnderAttachmentFolder(fullPath))
		{
			return true;
		}

		var relativePath = Path.GetRelativePath(layout.VaultRoot, fullPath);
		if (string.IsNullOrWhiteSpace(relativePath)
			|| string.Equals(relativePath, ".", StringComparison.Ordinal))
		{
			return false;
		}

		return relativePath
			.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
			.Any(static segment => segment.StartsWith("_", StringComparison.Ordinal) || segment.StartsWith(".", StringComparison.Ordinal));
	}

	/// <summary>
	/// Determines whether a freeform directive may assert its path as authoritative.
	/// </summary>
	public bool IsAllowedFreeformDirectiveAssertionPath(string path)
	{
		return TryGetFreeformDirectiveAssertionViolation(path) is null;
	}

	/// <summary>
	/// Gets a human-readable violation reason when a freeform directive assertion path is not allowed.
	/// </summary>
	public string? TryGetFreeformDirectiveAssertionViolation(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return "Path is empty.";
		}

		var fullPath = Path.GetFullPath(path);
		if (!IsUnderVaultRoot(fullPath))
		{
			return "Path is outside the active vault root.";
		}

		if (!string.Equals(Path.GetExtension(fullPath), ".md", StringComparison.OrdinalIgnoreCase))
		{
			return "Path is not a markdown file.";
		}

		var directory = Path.GetDirectoryName(fullPath);
		if (string.IsNullOrWhiteSpace(directory))
		{
			return "Path does not resolve to a directory.";
		}

		if (IsVaultRootDirectory(directory))
		{
			return "Freeform directive cannot assert ownership of the vault root directory.";
		}

		if (IsEntityRootDirectory(directory))
		{
			return "Freeform directive cannot assert ownership of an entity root directory.";
		}

		if (IsPartitionDirectory(directory))
		{
			return "Freeform directive cannot assert ownership of a partition directory.";
		}

		if (IsUnderNonDirectiveManagedRoot(directory))
		{
			return "Freeform directive path is under a managed root reserved for non-directive entities.";
		}

		var parent = Directory.GetParent(directory)?.FullName;
		if (!string.IsNullOrWhiteSpace(parent) && IsUnderNonDirectiveManagedRoot(parent))
		{
			return "Freeform directive parent directory is under a managed root reserved for non-directive entities.";
		}

		return null;
	}

	/// <summary>
	/// Resolves the nearest containing directive identifier while enforcing directive ownership boundaries.
	/// </summary>
	public string? TryResolveContainingDirectiveId(string? path, bool skipCurrentIfSelfNamed = false)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}

		var currentDirectory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
		if (skipCurrentIfSelfNamed
			&& !string.IsNullOrWhiteSpace(currentDirectory)
			&& MarkdownFileLocator.IsSelfNamedDirectory(currentDirectory))
		{
			currentDirectory = Directory.GetParent(currentDirectory)?.FullName;
		}

		while (!string.IsNullOrWhiteSpace(currentDirectory))
		{
			if (IsDirectiveOwnershipBoundaryDirectory(currentDirectory))
			{
				currentDirectory = Directory.GetParent(currentDirectory)?.FullName;
				continue;
			}

			var resolved = MarkdownFileLocator.TryResolveDirectivePuckFromDirectory(currentDirectory);
			if (!string.IsNullOrWhiteSpace(resolved))
			{
				return resolved;
			}

			currentDirectory = Directory.GetParent(currentDirectory)?.FullName;
		}

		return null;
	}

	private bool IsUnderVaultRoot(string fullPath)
	{
		var normalizedRoot = Path.GetFullPath(layout.VaultRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), normalizedRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
	}

	private bool IsUnderAttachmentFolder(string fullPath)
	{
		var attachmentFolderPath = TryResolveAttachmentFolderPath();
		if (string.IsNullOrWhiteSpace(attachmentFolderPath))
		{
			return false;
		}

		var normalizedFolder = attachmentFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		return fullPath.StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), normalizedFolder.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
	}

	private bool IsUnderNonDirectiveManagedRoot(string fullPath)
	{
		return IsUnderRoot(fullPath, layout.MetadataRoot)
			|| IsUnderRoot(fullPath, layout.ObjectivesRoot)
			|| IsUnderRoot(fullPath, layout.FatesRoot)
			|| IsUnderRoot(fullPath, layout.DecreesRoot)
			|| IsUnderRoot(fullPath, layout.OnrushRoot)
			|| IsUnderRoot(fullPath, layout.JournalRoot)
			|| IsUnderRoot(fullPath, layout.SagaRoot);
	}

	private bool IsDirectiveOwnershipBoundaryDirectory(string fullPath)
	{
		if (!IsUnderVaultRoot(fullPath))
		{
			return true;
		}

		return IsVaultRootDirectory(fullPath)
			|| IsEntityRootDirectory(fullPath)
			|| IsPartitionDirectory(fullPath)
			|| IsUnderNonDirectiveManagedRoot(fullPath);
	}

	private bool IsVaultRootDirectory(string fullPath)
	{
		var normalizedPath = Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var normalizedVaultRoot = Path.GetFullPath(layout.VaultRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return string.Equals(normalizedPath, normalizedVaultRoot, StringComparison.OrdinalIgnoreCase);
	}

	private bool IsEntityRootDirectory(string fullPath)
	{
		return EntityRoots().Any(root => IsDirectoryEqual(fullPath, root));
	}

	private static bool IsDirectoryEqual(string path, string other)
	{
		var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var normalizedOther = Path.GetFullPath(other).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return string.Equals(normalizedPath, normalizedOther, StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsPartitionDirectory(string fullPath)
	{
		var name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		return !string.IsNullOrWhiteSpace(name) && PartitionFolderNames.Contains(name);
	}

	private static bool IsUnderRoot(string fullPath, string rootPath)
	{
		var normalizedRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), normalizedRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
	}

	private string? TryResolveAttachmentFolderPath()
	{
		var obsidianConfigPath = Path.Combine(layout.VaultRoot, ObsidianConfigRelativePath.Replace('/', Path.DirectorySeparatorChar));
		if (!File.Exists(obsidianConfigPath))
		{
			return null;
		}

		try
		{
			using var stream = File.OpenRead(obsidianConfigPath);
			using var document = JsonDocument.Parse(stream);
			if (!document.RootElement.TryGetProperty("attachmentFolderPath", out var property)
				|| property.ValueKind != JsonValueKind.String)
			{
				return null;
			}

			var configuredPath = property.GetString();
			if (string.IsNullOrWhiteSpace(configuredPath)
				|| string.Equals(configuredPath, "./", StringComparison.Ordinal)
				|| string.Equals(configuredPath, "/", StringComparison.Ordinal)
				|| string.Equals(configuredPath, "current", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}

			var normalizedRelative = configuredPath
				.Replace('/', Path.DirectorySeparatorChar)
				.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
				.Trim();
			if (string.IsNullOrWhiteSpace(normalizedRelative))
			{
				return null;
			}

			return Path.GetFullPath(Path.Combine(layout.VaultRoot, normalizedRelative));
		}
		catch
		{
			return null;
		}
	}

	private static HashSet<string> ResolvePartitionFolderNames()
	{
		return typeof(VaultWatcherPathPolicy).Assembly
			.GetTypes()
			.Select(type => type.GetCustomAttribute<VaultStorageAttribute>())
			.Where(attribute => attribute is not null && !string.IsNullOrWhiteSpace(attribute.PartitionUnder))
			.Select(attribute => attribute!.PartitionUnder!.Trim())
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
	}
}
