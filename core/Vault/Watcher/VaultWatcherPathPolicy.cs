using System.Text.Json;
using Pleiades.Vault.Markdown;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Centralizes watcher path filtering and scan-root selection rules defined by the watcher blueprint.
/// </summary>
public sealed class VaultWatcherPathPolicy(VaultLayout layout)
{
	private const string ObsidianConfigRelativePath = ".obsidian/app.json";
	private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

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

		if (modelCatalog.GetModels().Any(static model => model.Mode == VaultStorageMode.Freeform))
		{
			roots.Add(Path.GetFullPath(layout.VaultRoot));
		}

		return roots
			.OrderByDescending(static root => root.Length)
			.ToArray();
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
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}

		var fullPath = Path.GetFullPath(path);
		if (!IsUnderVaultRoot(fullPath)
			|| !string.Equals(Path.GetExtension(fullPath), ".md", StringComparison.OrdinalIgnoreCase)
			|| !MarkdownFileLocator.IsPrimarySelfNamedFile(fullPath))
		{
			return false;
		}

		var directory = Path.GetDirectoryName(fullPath);
		if (string.IsNullOrWhiteSpace(directory))
		{
			return false;
		}

		if (IsUnderNonDirectiveManagedRoot(directory))
		{
			return false;
		}

		var parent = Directory.GetParent(directory)?.FullName;
		return string.IsNullOrWhiteSpace(parent) || !IsUnderNonDirectiveManagedRoot(parent);
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
			|| IsUnderRoot(fullPath, layout.OnrushRoot)
			|| IsUnderRoot(fullPath, layout.JournalRoot)
			|| IsUnderRoot(fullPath, layout.SagaRoot);
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
}
