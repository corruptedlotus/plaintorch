using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Saga;
using System.Reflection;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Database;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Centralizes path-based sync model detection for vault-backed entity types.
/// </summary>
public sealed class VaultPathSyncModelCatalog(VaultLayout layout)
{
	private static readonly string? ObjectivePartitionName = ResolvePartitionUnder(typeof(Objective));
	private static readonly string? FatePartitionName = ResolvePartitionUnder(typeof(Fate));
	private static readonly string? DecreePartitionName = ResolvePartitionUnder(typeof(Decree));
	private static readonly HashSet<string> PartitionFolderNames = ResolvePartitionFolderNames();

	private readonly IReadOnlyList<VaultPathSyncModel> _models =
	[
		// The directive family is polymorphic: the abstract Directive anchors identity/known-id lookups, while
		// path composition materializes a concrete StellarDirective (lunar directives are authored through the API).
		CreateModel<Directive>(layout, [layout.VaultRoot], VaultStorageShape.SelfNamedDirectory, static _ => false, static (context, cancellationToken) =>
			context.Directives
				.AsNoTracking()
				.Select(item => item.Id)
				.ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken),
			concreteType: typeof(StellarDirective)),

		CreateModel<Objective>(layout, [layout.ObjectivesRoot, layout.VaultRoot], VaultStorageShape.SingleFile, path =>
			IsIncentiveMarkdownFile(path, layout, layout.ObjectivesRoot, ObjectivePartitionName), static (context, cancellationToken) =>
			context.Objectives
				.AsNoTracking()
				.IgnoreAutoIncludes()
				.Select(item => item.Id)
				.ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken)),

		CreateModel<Fate>(layout, [layout.FatesRoot, layout.VaultRoot], VaultStorageShape.SingleFile, path =>
			IsIncentiveMarkdownFile(path, layout, layout.FatesRoot, FatePartitionName), static (context, cancellationToken) =>
			context.Fates
				.AsNoTracking()
				.IgnoreAutoIncludes()
				.Select(item => item.Id)
				.ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken)),

		CreateModel<Decree>(layout, [layout.DecreesRoot, layout.VaultRoot], VaultStorageShape.SingleFile, path =>
			IsIncentiveMarkdownFile(path, layout, layout.DecreesRoot, DecreePartitionName), static (context, cancellationToken) =>
			context.Decrees
				.AsNoTracking()
				.IgnoreAutoIncludes()
				.Select(item => item.Id)
				.ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken)),

		CreateModel<OnrushSprint>(layout, [layout.OnrushRoot], VaultStorageShape.SelfNamedDirectory, path =>
			IsPrimarySelfNamedEntityFile(path, layout.OnrushRoot), static (context, cancellationToken) =>
			context.OnrushSprints
				.AsNoTracking()
				.Select(item => item.Id)
				.ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken)),

		CreateModel<ExecutiveOrder>(layout, [layout.OnrushRoot], VaultStorageShape.SingleFile, static path =>
			IsExecutiveOrderMarkdownFile(path), static (context, cancellationToken) =>
			context.ExecutiveOrders
				.AsNoTracking()
				.Select(item => item.Id)
				.ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken)),

		CreateModel<PolarisCycle>(layout, [layout.JournalRoot], VaultStorageShape.SingleFile, static path =>
			string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase), static (context, cancellationToken) =>
			context.PolarisCycles
				.AsNoTracking()
				.Select(item => item.Id)
				.ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken)),

		CreateModel<LorePage>(layout, [layout.SagaRoot], VaultStorageShape.SelfNamedDirectory, path =>
			IsPrimarySelfNamedEntityFile(path, layout.SagaRoot), static (context, cancellationToken) =>
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
	/// <param name="model">The model whose scan roots and candidate predicate should be applied.</param>
	/// <returns>The distinct set of markdown paths currently matching the model.</returns>
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
	/// <param name="path">The raw filesystem path raised by watcher events.</param>
	/// <param name="markdownPath">Returns the markdown path to inspect when resolution succeeds.</param>
	/// <param name="model">Returns the matched sync model when resolution succeeds.</param>
	/// <returns><see langword="true"/> when a managed markdown candidate was resolved; otherwise <see langword="false"/>.</returns>
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
	/// <param name="path">The path to classify.</param>
	/// <param name="model">Returns the matched model when classification succeeds.</param>
	/// <returns><see langword="true"/> when a model matched; otherwise <see langword="false"/>.</returns>
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

	/// <summary>
	/// Creates a strongly-typed sync model from vault storage metadata and runtime lookup delegates.
	/// </summary>
	/// <typeparam name="T">The entity type represented by the model.</typeparam>
	/// <param name="layout">The active vault layout.</param>
	/// <param name="scanRoots">The roots to scan for candidates.</param>
	/// <param name="expectedShape">The expected storage shape for the entity.</param>
	/// <param name="isCandidatePath">A predicate identifying candidate markdown paths.</param>
	/// <param name="loadKnownIdsAsync">A delegate that loads known entity identifiers.</param>
	/// <returns>A configured path sync model for the entity type.</returns>
	private static VaultPathSyncModel CreateModel<T>(
		VaultLayout layout,
		IReadOnlyList<string> scanRoots,
		VaultStorageShape expectedShape,
		Func<string, bool> isCandidatePath,
		Func<PlainfraContext, CancellationToken, Task<HashSet<string>>> loadKnownIdsAsync,
		Type? concreteType = null)
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
			loadKnownIdsAsync,
			concreteType);
	}

	/// <summary>
	/// Determines whether a full path is located under a specific scan root.
	/// </summary>
	/// <param name="fullPath">The path being tested.</param>
	/// <param name="rootPath">The candidate root path.</param>
	/// <returns><see langword="true"/> when the path is under the root; otherwise <see langword="false"/>.</returns>
	private static bool IsPathUnderRoot(string fullPath, string rootPath)
	{
		var normalizedRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Determines whether a markdown file path should be classified as an incentive file of a specific kind
	/// (objective, fate, or decree). Each kind lives in its standalone root, directly inside a directive
	/// directory, or inside its own partition folder within a directive.
	/// </summary>
	/// <param name="path">The markdown file path to classify.</param>
	/// <param name="layout">The active vault layout.</param>
	/// <param name="standaloneRoot">The kind's standalone root directory.</param>
	/// <param name="partitionName">The kind's directive partition folder name.</param>
	/// <returns><see langword="true"/> when the file is a candidate of this incentive kind.</returns>
	private static bool IsIncentiveMarkdownFile(string path, VaultLayout layout, string standaloneRoot, string? partitionName)
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

		if (string.Equals(parentDirectory, standaloneRoot, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		// Another incentive kind's standalone root or partition folder is never a candidate here;
		// identity-driven belonging would reject it anyway, but the path gate keeps candidates tight.
		if (IsForeignIncentiveContainer(parentDirectory, layout, standaloneRoot, partitionName))
		{
			return false;
		}

		if (MarkdownFileLocator.IsSelfNamedDirectory(parentDirectory))
		{
			return true;
		}

		if (string.IsNullOrWhiteSpace(TryResolveContainingDirectiveIdWithOwnershipBoundaries(path, layout, skipCurrentIfSelfNamed: false)))
		{
			return false;
		}

		if (string.IsNullOrWhiteSpace(partitionName))
		{
			return true;
		}

		var containingDirectoryName = Path.GetFileName(parentDirectory);
		if (string.Equals(containingDirectoryName, partitionName, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return false;
	}

	/// <summary>
	/// Determines whether a directory belongs to a different incentive kind (its standalone root or
	/// partition folder) than the one being classified.
	/// </summary>
	private static bool IsForeignIncentiveContainer(string directory, VaultLayout layout, string ownRoot, string? ownPartition)
	{
		foreach (var root in new[] { layout.ObjectivesRoot, layout.FatesRoot, layout.DecreesRoot })
		{
			if (!string.Equals(root, ownRoot, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(directory, root, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		var directoryName = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		if (string.IsNullOrWhiteSpace(directoryName) || !PartitionFolderNames.Contains(directoryName))
		{
			return false;
		}

		return !string.Equals(directoryName, ownPartition, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Determines whether a markdown file path should be classified as an executive order file.
	/// Order files live inside the order partition folder of a self-named onrush sprint directory.
	/// </summary>
	/// <param name="path">The markdown file path to classify.</param>
	/// <returns><see langword="true"/> when the file is an executive order markdown candidate.</returns>
	private static bool IsExecutiveOrderMarkdownFile(string path)
	{
		if (!string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		if (MarkdownFileLocator.IsPrimarySelfNamedFile(path))
		{
			return false;
		}

		return !string.IsNullOrWhiteSpace(MarkdownFileLocator.TryGetContainingOnrushSprintId(path));
	}

	private static string? TryResolveContainingDirectiveIdWithOwnershipBoundaries(string? path, VaultLayout layout, bool skipCurrentIfSelfNamed)
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
			if (IsDirectiveOwnershipBoundaryDirectory(currentDirectory, layout))
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

	private static bool IsDirectiveOwnershipBoundaryDirectory(string directoryPath, VaultLayout layout)
	{
		if (string.IsNullOrWhiteSpace(directoryPath))
		{
			return true;
		}

		if (!IsPathUnderRoot(directoryPath, layout.VaultRoot))
		{
			return true;
		}

		if (IsDirectoryEqual(directoryPath, layout.VaultRoot)
			|| IsDirectoryEqual(directoryPath, layout.DirectivesRoot)
			|| IsDirectoryEqual(directoryPath, layout.ObjectivesRoot)
			|| IsDirectoryEqual(directoryPath, layout.FatesRoot)
			|| IsDirectoryEqual(directoryPath, layout.DecreesRoot)
			|| IsDirectoryEqual(directoryPath, layout.OnrushRoot)
			|| IsDirectoryEqual(directoryPath, layout.JournalRoot)
			|| IsDirectoryEqual(directoryPath, layout.SagaRoot)
			|| IsDirectoryEqual(directoryPath, layout.MetadataRoot))
		{
			return true;
		}

		var directoryName = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		if (!string.IsNullOrWhiteSpace(directoryName) && PartitionFolderNames.Contains(directoryName))
		{
			return true;
		}

		if (IsPathUnderRoot(directoryPath, layout.ObjectivesRoot)
			|| IsPathUnderRoot(directoryPath, layout.FatesRoot)
			|| IsPathUnderRoot(directoryPath, layout.DecreesRoot)
			|| IsPathUnderRoot(directoryPath, layout.OnrushRoot)
			|| IsPathUnderRoot(directoryPath, layout.JournalRoot)
			|| IsPathUnderRoot(directoryPath, layout.SagaRoot)
			|| IsPathUnderRoot(directoryPath, layout.MetadataRoot))
		{
			return true;
		}

		return false;
	}

	private static bool IsDirectoryEqual(string path, string other)
	{
		var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var normalizedOther = Path.GetFullPath(other).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return string.Equals(normalizedPath, normalizedOther, StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsPartitionContainerPrimaryFile(string path)
	{
		if (!MarkdownFileLocator.IsPrimarySelfNamedFile(path))
		{
			return false;
		}

		var containerDirectory = Path.GetDirectoryName(path);
		if (string.IsNullOrWhiteSpace(containerDirectory))
		{
			return false;
		}

		var directoryName = Path.GetFileName(containerDirectory);
		if (string.IsNullOrWhiteSpace(directoryName)
			|| !PartitionFolderNames.Contains(directoryName))
		{
			return false;
		}

		var parentDirectory = Directory.GetParent(containerDirectory)?.FullName;
		return !string.IsNullOrWhiteSpace(parentDirectory)
			&& MarkdownFileLocator.IsSelfNamedDirectory(parentDirectory);
	}

	private static bool IsPrimarySelfNamedEntityFile(string path, string scanRoot)
	{
		if (!MarkdownFileLocator.IsPrimarySelfNamedFile(path))
		{
			return false;
		}

		var parentDirectory = Path.GetDirectoryName(path);
		if (string.IsNullOrWhiteSpace(parentDirectory))
		{
			return false;
		}

		var normalizedParent = Path.GetFullPath(parentDirectory)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var normalizedScanRoot = Path.GetFullPath(scanRoot)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		return !string.Equals(normalizedParent, normalizedScanRoot, StringComparison.OrdinalIgnoreCase);
	}

	private static string? ResolvePartitionUnder(Type entityType)
	{
		return entityType
			.GetCustomAttribute<VaultStorageAttribute>()
			?.PartitionUnder
			?.Trim();
	}

	private static HashSet<string> ResolvePartitionFolderNames()
	{
		return typeof(VaultPathSyncModelCatalog).Assembly
			.GetTypes()
			.Select(type => type.GetCustomAttribute<VaultStorageAttribute>())
			.Where(attribute => attribute is not null && !string.IsNullOrWhiteSpace(attribute.PartitionUnder))
			.Select(attribute => attribute!.PartitionUnder!.Trim())
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
	}
}