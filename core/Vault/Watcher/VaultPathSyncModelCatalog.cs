using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
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
	];

	/// <summary>
	/// Gets all known path-resolvable sync models.
	/// </summary>
	public IReadOnlyList<VaultPathSyncModel> GetModels() => _models;

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