using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Saga;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Database;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Centralizes path-based sync model detection for vault-backed entity types.
/// </summary>
public sealed class VaultPathSyncModelCatalog(VaultLayout layout)
{
	private readonly IReadOnlyList<VaultPathSyncModel> _models =
	[
		CreateModel<Directive>(layout, [layout.DirectivesRoot], VaultStorageShape.SelfNamedDirectory, static path =>
			MarkdownFileLocator.IsPrimarySelfNamedFile(path), static (context, cancellationToken) =>
			context.Directives
				.AsNoTracking()
				.Select(item => item.Id)
				.ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken)),

		CreateModel<Objective>(layout, [layout.ObjectivesRoot, layout.DirectivesRoot], VaultStorageShape.SingleFile, path =>
			IsObjectiveMarkdownFile(path, layout), static (context, cancellationToken) =>
			context.Objectives
				.AsNoTracking()
				.Select(item => item.Id)
				.ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken)),

		CreateModel<OnrushSprint>(layout, [layout.OnrushRoot], VaultStorageShape.SelfNamedDirectory, static path =>
			MarkdownFileLocator.IsPrimarySelfNamedFile(path), static (context, cancellationToken) =>
			context.OnrushSprints
				.AsNoTracking()
				.Select(item => item.Id)
				.ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken)),

		CreateModel<PolarisCycle>(layout, [layout.JournalRoot], VaultStorageShape.SingleFile, static path =>
			string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase), static (context, cancellationToken) =>
			context.PolarisCycles
				.AsNoTracking()
				.Select(item => item.Id)
				.ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken)),

		CreateModel<LorePage>(layout, [layout.SagaRoot], VaultStorageShape.SelfNamedDirectory, static path =>
			MarkdownFileLocator.IsPrimarySelfNamedFile(path), static (context, cancellationToken) =>
			context.LorePages
				.AsNoTracking()
				.Select(item => item.Id)
				.ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken)),
	];

	/// <summary>
	/// Gets all known path-resolvable sync models.
	/// </summary>
	public IReadOnlyList<VaultPathSyncModel> GetModels() => _models;

	/// <summary>
	/// Gets all distinct scan roots ordered by specificity.
	/// </summary>
	public IReadOnlyList<string> GetScanRoots()
	{
		return _models
			.SelectMany(model => model.ScanRoots)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderByDescending(root => Path.GetFullPath(root).Length)
			.ToList();
	}

	/// <summary>
	/// Enumerates all existing markdown candidates discoverable by model rules.
	/// </summary>
	public IReadOnlyList<string> EnumerateCandidateMarkdownPaths()
	{
		return _models
			.SelectMany(model => EnumerateCandidateMarkdownPaths(model))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	/// <summary>
	/// Enumerates existing markdown candidates discoverable for a specific model.
	/// </summary>
	public IReadOnlyList<string> EnumerateCandidateMarkdownPaths(VaultPathSyncModel model)
	{
		ArgumentNullException.ThrowIfNull(model);
		return model.ScanRoots
			.Where(Directory.Exists)
			.SelectMany(root => Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
			.Where(model.IsCandidatePath)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	/// <summary>
	/// Resolves an incoming watcher path to an inspectable markdown path and model.
	/// </summary>
	public bool TryResolveWatchPath(string path, out string? markdownPath, out VaultPathSyncModel? model)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		var fullPath = Path.GetFullPath(path);

		if (TryResolve(fullPath, out model) && model is not null)
		{
			markdownPath = fullPath;
			return true;
		}

		if (!Directory.Exists(fullPath))
		{
			markdownPath = null;
			model = null;
			return false;
		}

		var directoryName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		if (string.IsNullOrWhiteSpace(directoryName))
		{
			markdownPath = null;
			model = null;
			return false;
		}

		var selfNamedPrimary = Path.Combine(fullPath, $"{directoryName}.md");
		model = _models
			.Where(candidate => candidate.Shape == VaultStorageShape.SelfNamedDirectory)
			.OrderByDescending(candidate => candidate.ScanRoots.Max(root => root.Length))
			.FirstOrDefault(candidate =>
				candidate.ScanRoots.Any(root => IsPathUnderRoot(fullPath, root))
				&& candidate.IsCandidatePath(selfNamedPrimary));

		if (model is null)
		{
			markdownPath = null;
			return false;
		}

		markdownPath = selfNamedPrimary;
		return true;
	}

	/// <summary>
	/// Resolves a sync model for a markdown path using only path location and file shape.
	/// </summary>
	public bool TryResolve(string path, out VaultPathSyncModel? model)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		var fullPath = Path.GetFullPath(path);
		model = _models
			.OrderByDescending(candidate => candidate.ScanRoots.Max(root => root.Length))
			.FirstOrDefault(candidate =>
				candidate.ScanRoots.Any(root => IsPathUnderRoot(fullPath, root))
				&& candidate.IsCandidatePath(fullPath));

		return model is not null;
	}

	private static VaultPathSyncModel CreateModel<T>(
		VaultLayout layout,
		IReadOnlyList<string> scanRoots,
		VaultStorageShape expectedShape,
		Func<string, bool> isCandidatePath,
		Func<PlainfraContext, CancellationToken, Task<HashSet<string>>> loadKnownIdsAsync)
	{
		var attribute = typeof(T).GetCustomAttributes(typeof(VaultStorageAttribute), inherit: true)
			.OfType<VaultStorageAttribute>()
			.SingleOrDefault()
			?? throw new InvalidOperationException($"Type '{typeof(T).Name}' must declare {nameof(VaultStorageAttribute)} to participate in path-based sync detection.");

		if (attribute.Shape != expectedShape)
		{
			throw new InvalidOperationException($"Type '{typeof(T).Name}' declares storage shape '{attribute.Shape}', but the sync model catalog expected '{expectedShape}'.");
		}

		return new VaultPathSyncModel(
			typeof(T),
			scanRoots,
			attribute.Mode,
			attribute.Shape,
			isCandidatePath,
			loadKnownIdsAsync);
	}

	private static bool IsPathUnderRoot(string fullPath, string rootPath)
	{
		var normalizedRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsObjectiveMarkdownFile(string path, VaultLayout layout)
	{
		if (!string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		if (MarkdownFileLocator.IsPrimarySelfNamedFile(path))
		{
			return false;
		}

		var parentDirectory = Path.GetDirectoryName(path);
		if (string.IsNullOrWhiteSpace(parentDirectory))
		{
			return false;
		}

		return string.Equals(parentDirectory, layout.ObjectivesRoot, StringComparison.OrdinalIgnoreCase)
			|| MarkdownFileLocator.IsSelfNamedDirectory(parentDirectory);
	}
}