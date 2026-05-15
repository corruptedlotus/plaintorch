using System.Reflection;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Vault;
using Pleiades.Vault.Database;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Watcher;
using Microsoft.EntityFrameworkCore;

namespace Pleiades.Plaintorch.Markdown;

/// <summary>
/// Synchronizes vault-backed markdown files for application API actions while preserving existing body content verbatim.
/// The service only rewrites frontmatter and canonical file identity; it never synthesizes default markdown body content.
/// </summary>
public sealed class PlaintorchMarkdownStorageService(
	PlainfraContext context,
	VaultLayout layout,
	MarkdownFrontMatterSerializer markdownSerializer,
	MarkdownFileLocator markdownFileLocator,
	VaultTemporalDataService temporalDataService,
	VaultWatcherWriteBarrier writeBarrier)
{
	private static readonly MethodInfo FindAsyncMethod = typeof(DbContext)
		.GetMethods(BindingFlags.Public | BindingFlags.Instance)
		.Single(method => method.Name == nameof(DbContext.FindAsync)
			&& method.GetParameters().Length == 2
			&& method.GetParameters()[0].ParameterType == typeof(Type)
			&& method.GetParameters()[1].ParameterType == typeof(object[]));

	/// <summary>
	/// Writes the canonical markdown file for a directive.
	/// </summary>
	public async Task SaveDirectiveAsync(Directive directive, Directive? previous = null, string? sourcePath = null, CancellationToken cancellationToken = default)
	{
		await SaveCanonicalMarkdownAsync(directive, previous, sourcePath, cancellationToken);
	}

	/// <summary>
	/// Deletes a directive markdown file.
	/// </summary>
	public Task<FileGraveyardEntry?> DeleteDirectiveAsync(Directive directive, CancellationToken cancellationToken = default)
	{
		return DeleteEntityPathAsync(directive, cancellationToken);
	}

	/// <summary>
	/// Writes the canonical markdown file for an objective.
	/// </summary>
	public async Task SaveObjectiveAsync(Objective objective, Objective? previous = null, string? sourcePath = null, CancellationToken cancellationToken = default)
	{
		await SaveCanonicalMarkdownAsync(objective, previous, sourcePath, cancellationToken);
	}

	/// <summary>
	/// Deletes an objective markdown file.
	/// </summary>
	public Task<FileGraveyardEntry?> DeleteObjectiveAsync(Objective objective, CancellationToken cancellationToken = default)
	{
		return DeleteEntityPathAsync(objective, cancellationToken);
	}

	/// <summary>
	/// Writes the canonical markdown file for an onrush sprint.
	/// </summary>
	public async Task SaveOnrushSprintAsync(OnrushSprint sprint, OnrushSprint? previous = null, string? sourcePath = null, CancellationToken cancellationToken = default)
	{
		await SaveCanonicalMarkdownAsync(sprint, previous, sourcePath, cancellationToken);
	}

	/// <summary>
	/// Deletes an onrush sprint markdown file.
	/// </summary>
	public Task<FileGraveyardEntry?> DeleteOnrushSprintAsync(OnrushSprint sprint, CancellationToken cancellationToken = default)
	{
		return DeleteEntityPathAsync(sprint, cancellationToken);
	}

	/// <summary>
	/// Writes the canonical markdown file for a Polaris cycle.
	/// </summary>
	public async Task SavePolarisCycleAsync(PolarisCycle cycle, PolarisCycle? previous = null, string? sourcePath = null, CancellationToken cancellationToken = default)
	{
		await SaveCanonicalMarkdownAsync(cycle, previous, sourcePath, cancellationToken);
	}

	/// <summary>
	/// Deletes a Polaris cycle markdown file.
	/// </summary>
	public Task<FileGraveyardEntry?> DeletePolarisCycleAsync(PolarisCycle cycle, CancellationToken cancellationToken = default)
	{
		return DeleteEntityPathAsync(cycle, cancellationToken);
	}

	private async Task SaveCanonicalMarkdownAsync(object entity, object? previous, string? sourcePath, CancellationToken cancellationToken)
	{
		var previousPath = previous is null ? null : await ResolveCanonicalPathAsync(previous, cancellationToken);
		var newPath = await ResolveCanonicalPathAsync(entity, cancellationToken);
		var body = await ResolveBodyAsync(previousPath, sourcePath, newPath, cancellationToken);
		await WriteMarkdownAsync(newPath, markdownSerializer.Serialize(entity, body), cancellationToken);
		SuppressWatcherPaths(newPath, previousPath, sourcePath);
		DeleteOldPath(previousPath, newPath, ResolveStorageRoot(entity.GetType()));
		DeleteSourcePath(sourcePath, newPath);
	}

	private async Task<FileGraveyardEntry?> DeleteEntityPathAsync(object entity, CancellationToken cancellationToken)
	{
		var path = await ResolveCanonicalPathAsync(entity, cancellationToken);
		var namedEntity = entity as IPuckNamedEntity;
		return await DeletePathAsync(path, ResolveStorageRoot(entity.GetType()), entity.GetType().Name, namedEntity?.Id, namedEntity?.Title, "api-delete", cancellationToken);
	}

	private async Task<string> ResolveCanonicalPathAsync(object entity, CancellationToken cancellationToken)
	{
		var parent = await LoadParentHierarchyAsync(entity, cancellationToken);
		return markdownFileLocator.GetFilePath(entity, parent);
	}

	private async Task<object?> LoadParentHierarchyAsync(object entity, CancellationToken cancellationToken)
	{
		var storage = GetStorageAttribute(entity.GetType());
		if (string.IsNullOrWhiteSpace(storage.ParentIdProperty) || storage.ParentEntityType is null)
		{
			return null;
		}

		var parentId = entity.GetType().GetProperty(storage.ParentIdProperty, BindingFlags.Public | BindingFlags.Instance)?.GetValue(entity) as string;
		if (string.IsNullOrWhiteSpace(parentId))
		{
			return null;
		}

		var parent = await FindEntityAsync(storage.ParentEntityType, parentId, cancellationToken);
		if (parent is null)
		{
			return null;
		}

		var parentParent = await LoadParentHierarchyAsync(parent, cancellationToken);
		HydrateParentNavigation(parent, parentParent);
		return parent;
	}

	private async Task<object?> FindEntityAsync(Type entityType, string id, CancellationToken cancellationToken)
	{
		var valueTask = (dynamic)FindAsyncMethod.Invoke(context, [entityType, new object[] { id }])!;
		var entity = await valueTask.AsTask().WaitAsync(cancellationToken);
		if (entity is not null)
		{
			context.Entry(entity).State = EntityState.Detached;
		}

		return entity;
	}

	private static void HydrateParentNavigation(object entity, object? parent)
	{
		if (parent is null)
		{
			return;
		}

		var storage = GetStorageAttribute(entity.GetType());
		if (string.IsNullOrWhiteSpace(storage.ParentIdProperty) || storage.ParentEntityType is null)
		{
			return;
		}

		var navigationName = storage.ParentIdProperty.EndsWith("Id", StringComparison.Ordinal)
			? storage.ParentIdProperty[..^2]
			: storage.ParentEntityType.Name;

		var navigation = entity.GetType().GetProperty(navigationName, BindingFlags.Public | BindingFlags.Instance);
		if (navigation?.CanWrite == true && navigation.PropertyType.IsAssignableFrom(storage.ParentEntityType))
		{
			navigation.SetValue(entity, parent);
		}
	}

	private static VaultStorageAttribute GetStorageAttribute(Type entityType)
	{
		return entityType.GetCustomAttribute<VaultStorageAttribute>()
			?? throw new InvalidOperationException($"Type '{entityType.Name}' is not configured for vault markdown storage.");
	}

	private string ResolveStorageRoot(Type entityType)
	{
		return layout.GetLocationRoot(GetStorageAttribute(entityType).LocationKey);
	}

	private void SuppressWatcherPaths(params string?[] paths)
	{
		foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
		{
			writeBarrier.Suppress(path!);
		}
	}

	private static async Task WriteMarkdownAsync(string path, string markdown, CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		if (File.Exists(path))
		{
			var existing = await File.ReadAllTextAsync(path, cancellationToken);
			if (string.Equals(existing, markdown, StringComparison.Ordinal))
			{
				return;
			}
		}

		await File.WriteAllTextAsync(path, markdown, cancellationToken);
	}

	private static async Task<string> ResolveBodyAsync(string? previousPath, string? sourcePath, string currentPath, CancellationToken cancellationToken)
	{
		var candidatePath = previousPath is not null && File.Exists(previousPath)
			? previousPath
			: sourcePath is not null && File.Exists(sourcePath)
				? sourcePath
			: File.Exists(currentPath)
				? currentPath
				: null;

		if (candidatePath is null)
		{
			return string.Empty;
		}

		var markdown = await File.ReadAllTextAsync(candidatePath, cancellationToken);
		return ExtractBody(markdown);
	}

	private static string ExtractBody(string markdown)
	{
		if (!markdown.StartsWith("---", StringComparison.Ordinal))
		{
			return markdown;
		}

		using var reader = new StringReader(markdown);
		reader.ReadLine();
		while (reader.ReadLine() is { } line)
		{
			if (line == "---")
			{
				break;
			}
		}

		return reader.ReadToEnd().TrimStart('\r', '\n');
	}

	private static void DeleteOldPath(string? previousPath, string currentPath, string rootPath)
	{
		if (string.IsNullOrWhiteSpace(previousPath) || string.Equals(previousPath, currentPath, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		DeletePath(previousPath, rootPath);
	}

	private void DeleteSourcePath(string? sourcePath, string currentPath)
	{
		if (string.IsNullOrWhiteSpace(sourcePath))
		{
			return;
		}

		var fullSourcePath = Path.GetFullPath(sourcePath);
		if (string.Equals(fullSourcePath, currentPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullSourcePath))
		{
			return;
		}

		DeletePath(fullSourcePath, ResolveRootPath(fullSourcePath));
	}

	private string ResolveRootPath(string path)
	{
		var fullPath = Path.GetFullPath(path);
		var candidateRoots = new[]
		{
			layout.DirectivesRoot,
			layout.ObjectivesRoot,
			layout.OnrushRoot,
			layout.JournalRoot,
		};

		var matchingRoot = candidateRoots
			.Where(root => fullPath.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
			.OrderByDescending(root => root.Length)
			.FirstOrDefault();

		return matchingRoot ?? Path.GetDirectoryName(fullPath) ?? layout.VaultRoot;
	}

	private async Task<FileGraveyardEntry?> DeletePathAsync(
		string path,
		string rootPath,
		string? entityType,
		string? entityId,
		string? entityTitle,
		string reason,
		CancellationToken cancellationToken)
	{
		var archiveTarget = ResolveArchiveTarget(path);
		if (archiveTarget is null)
		{
			DeletePath(path, rootPath);
			return null;
		}

		var resolvedArchiveTarget = archiveTarget.Value;

		var cleanupRoot = resolvedArchiveTarget.IsDirectory
			? Directory.GetParent(resolvedArchiveTarget.Path)?.FullName
			: Path.GetDirectoryName(resolvedArchiveTarget.Path);

		var entry = await temporalDataService.ArchivePathAsync(
			resolvedArchiveTarget.Path,
			reason,
			entityType,
			entityId,
			entityTitle,
			archivedBy: Environment.UserName,
			cancellationToken);

		writeBarrier.Suppress(resolvedArchiveTarget.Path);

		PruneEmptyDirectories(cleanupRoot, rootPath);
		return entry;
	}

	private static (string Path, bool IsDirectory)? ResolveArchiveTarget(string path)
	{
		if (File.Exists(path))
		{
			var directory = Path.GetDirectoryName(path)!;
			var selfNamedDirectory = string.Equals(Path.GetFileNameWithoutExtension(path), Path.GetFileName(directory), StringComparison.OrdinalIgnoreCase);
			if (selfNamedDirectory && Directory.Exists(directory))
			{
				return (directory, true);
			}

			return (path, false);
		}

		return Directory.Exists(path)
			? (path, true)
			: null;
	}

	private static void DeletePath(string path, string rootPath)
	{
		if (File.Exists(path))
		{
			File.Delete(path);
		}

		PruneEmptyDirectories(Path.GetDirectoryName(path), rootPath);
	}

	private static void PruneEmptyDirectories(string? startDirectory, string rootPath)
	{
		var currentDirectory = startDirectory;
		while (!string.IsNullOrWhiteSpace(currentDirectory)
			&& !string.Equals(currentDirectory, rootPath, StringComparison.OrdinalIgnoreCase)
			&& Directory.Exists(currentDirectory)
			&& !Directory.EnumerateFileSystemEntries(currentDirectory).Any())
		{
			var parent = Directory.GetParent(currentDirectory)?.FullName;
			Directory.Delete(currentDirectory);
			currentDirectory = parent;
		}
	}

}
