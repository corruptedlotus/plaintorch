using System.Reflection;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Saga;
using Pleiades.Vault;
using Pleiades.Vault.Database;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Policy;
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
	VaultPathSyncModelCatalog pathSyncModelCatalog,
	VaultWatcherPathPolicy pathPolicy,
	VaultTemporalDataService temporalDataService,
	VaultImplicitBoundaryService implicitBoundaryService,
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
	/// Re-emits an entity's canonical markdown from its current conventions, converting a legacy file in place.
	/// </summary>
	/// <remarks>
	/// This is the re-canonicalisation primitive used by vault migrations. The legacy file at <paramref name="legacyPath"/>
	/// is used as the body/frontmatter source and is renamed/relocated/rewritten to the current canonical form, preserving
	/// its markdown body verbatim. Passing <paramref name="beginBoundary"/> is required for implicit entities so their file
	/// materializes and their synchronization boundary begins.
	/// </remarks>
	/// <param name="entity">The entity to re-canonicalise.</param>
	/// <param name="legacyPath">The legacy markdown file to convert, when known.</param>
	/// <param name="beginBoundary">Whether to begin an implicit entity's synchronization boundary.</param>
	/// <param name="cancellationToken">A token used to cancel the operation.</param>
	public async Task RecanonicalizeAsync(object entity, string? legacyPath = null, bool beginBoundary = false, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entity);
		await SaveCanonicalMarkdownAsync(entity, previous: null, sourcePath: legacyPath, cancellationToken, beginBoundary);
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
	/// <param name="beginBoundary">
	/// When <see langword="true"/>, materializes the file for an implicit entity even if its synchronization boundary has
	/// not begun yet, thereby beginning that boundary. Ordinary saves leave implicit entities unmaterialized until prompted.
	/// </param>
	public async Task SaveObjectiveAsync(Objective objective, Objective? previous = null, string? sourcePath = null, bool beginBoundary = false, CancellationToken cancellationToken = default)
	{
		await SaveCanonicalMarkdownAsync(objective, previous, sourcePath, cancellationToken, beginBoundary);
	}

	/// <summary>
	/// Deletes an objective markdown file.
	/// </summary>
	public Task<FileGraveyardEntry?> DeleteObjectiveAsync(Objective objective, CancellationToken cancellationToken = default)
	{
		return DeleteEntityPathAsync(objective, cancellationToken);
	}

	/// <summary>
	/// Writes the canonical markdown file for a fate declarative.
	/// </summary>
	/// <param name="beginBoundary">
	/// When <see langword="true"/>, materializes the file for the implicit entity even if its synchronization
	/// boundary has not begun yet, thereby beginning that boundary.
	/// </param>
	public async Task SaveFateAsync(Fate fate, Fate? previous = null, string? sourcePath = null, bool beginBoundary = false, CancellationToken cancellationToken = default)
	{
		await SaveCanonicalMarkdownAsync(fate, previous, sourcePath, cancellationToken, beginBoundary);
	}

	/// <summary>
	/// Deletes a fate markdown file.
	/// </summary>
	public Task<FileGraveyardEntry?> DeleteFateAsync(Fate fate, CancellationToken cancellationToken = default)
	{
		return DeleteEntityPathAsync(fate, cancellationToken);
	}

	/// <summary>
	/// Writes the canonical markdown file for a decree declarative.
	/// </summary>
	/// <param name="beginBoundary">
	/// When <see langword="true"/>, materializes the file for the implicit entity even if its synchronization
	/// boundary has not begun yet, thereby beginning that boundary.
	/// </param>
	public async Task SaveDecreeAsync(Decree decree, Decree? previous = null, string? sourcePath = null, bool beginBoundary = false, CancellationToken cancellationToken = default)
	{
		await SaveCanonicalMarkdownAsync(decree, previous, sourcePath, cancellationToken, beginBoundary);
	}

	/// <summary>
	/// Deletes a decree markdown file.
	/// </summary>
	public Task<FileGraveyardEntry?> DeleteDecreeAsync(Decree decree, CancellationToken cancellationToken = default)
	{
		return DeleteEntityPathAsync(decree, cancellationToken);
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
	/// Writes the canonical markdown file for an executive order.
	/// </summary>
	public async Task SaveExecutiveOrderAsync(ExecutiveOrder order, ExecutiveOrder? previous = null, string? sourcePath = null, CancellationToken cancellationToken = default)
	{
		await SaveCanonicalMarkdownAsync(order, previous, sourcePath, cancellationToken);
	}

	/// <summary>
	/// Deletes an executive order markdown file.
	/// </summary>
	public Task<FileGraveyardEntry?> DeleteExecutiveOrderAsync(ExecutiveOrder order, CancellationToken cancellationToken = default)
	{
		return DeleteEntityPathAsync(order, cancellationToken);
	}

	/// <summary>
	/// Writes the canonical markdown file for a Polaris cycle.
	/// </summary>
	public async Task SavePolarisCycleAsync(PolarisCycle cycle, PolarisCycle? previous = null, string? sourcePath = null, CancellationToken cancellationToken = default)
	{
		await SaveCanonicalMarkdownAsync(cycle, previous, sourcePath, cancellationToken);
	}

	/// <summary>
	/// Writes the canonical markdown file for a lore page.
	/// </summary>
	public async Task SaveLorePageAsync(LorePage lorePage, LorePage? previous = null, string? sourcePath = null, CancellationToken cancellationToken = default)
	{
		await SaveCanonicalMarkdownAsync(lorePage, previous, sourcePath, cancellationToken);
	}

	/// <summary>
	/// Deletes a lore page's self-named directory and markdown file.
	/// </summary>
	public Task<FileGraveyardEntry?> DeleteLorePageAsync(LorePage lorePage, CancellationToken cancellationToken = default)
	{
		return DeleteEntityPathAsync(lorePage, cancellationToken);
	}

	/// <summary>
	/// Deletes a Polaris cycle markdown file.
	/// </summary>
	public Task<FileGraveyardEntry?> DeletePolarisCycleAsync(PolarisCycle cycle, CancellationToken cancellationToken = default)
	{
		return DeleteEntityPathAsync(cycle, cancellationToken);
	}

	private async Task SaveCanonicalMarkdownAsync(object entity, object? previous, string? sourcePath, CancellationToken cancellationToken, bool beginBoundary = false)
	{
		var storage = GetStorageAttribute(entity.GetType());
		var previousPath = previous is null
			? await TryResolveExistingPathByIdentityAsync(entity, sourcePath, cancellationToken)
			: await ResolveCanonicalPathAsync(previous, cancellationToken);
		var newPath = await ResolveCanonicalPathAsync(entity, cancellationToken);
		if (previous is null
			&& string.IsNullOrWhiteSpace(sourcePath)
			&& !string.IsNullOrWhiteSpace(previousPath)
			&& File.Exists(previousPath))
		{
			newPath = previousPath;
		}

		if (string.IsNullOrWhiteSpace(previousPath))
		{
			var existingPath = await TryResolveExistingPathByIdentityAsync(entity, sourcePath, cancellationToken);
			if (!string.IsNullOrWhiteSpace(existingPath))
			{
				previousPath = existingPath;
			}
		}

		if (storage.Mode == VaultStorageMode.Implicit
			&& !beginBoundary
			&& !await IsImplicitBoundaryMaterializedAsync(entity, previousPath, sourcePath, cancellationToken))
		{
			// Implicit entities do not initially sync to a file; withhold materialization until the boundary is begun.
			return;
		}

		newPath = ResolveInactionPreferredPath(entity, newPath, sourcePath);
		newPath = ResolveFreeformTargetPath(entity, newPath, sourcePath);
		if (entity is LorePage lorePage && previous is LorePage previousLorePage
			&& !string.Equals(lorePage.ParentId, previousLorePage.ParentId, StringComparison.OrdinalIgnoreCase))
		{
			newPath = await ResolveLoreParentReassignmentPathAsync(lorePage, cancellationToken);
			lorePage.RelativePath = Path.GetRelativePath(layout.VaultRoot, newPath);
		}

		previousPath = TryRelocateSelfNamedDirectory(previousPath, newPath, entity.GetType());
		var body = await ResolveBodyAsync(previousPath, sourcePath, newPath, cancellationToken);
		var preservedFrontMatter = await ResolveFrontMatterAsync(previousPath, sourcePath, newPath, cancellationToken);
		SuppressWatcherPaths(
			newPath,
			previousPath,
			sourcePath,
			Path.GetDirectoryName(newPath),
			Path.GetDirectoryName(previousPath),
			Path.GetDirectoryName(sourcePath));
		await WriteMarkdownAsync(newPath, markdownSerializer.Serialize(entity, body, preservedFrontMatter), cancellationToken);
		DeleteOldPath(previousPath, newPath, ResolveStorageRoot(entity.GetType()));
		DeleteSourcePath(sourcePath, newPath);

		if (storage.Mode == VaultStorageMode.Implicit
			&& entity is IPuckNamedEntity boundaryEntity
			&& !string.IsNullOrWhiteSpace(boundaryEntity.Id)
			&& File.Exists(newPath))
		{
			await implicitBoundaryService.EnsureBoundaryBegunAsync(
				entity.GetType().Name,
				boundaryEntity.Id,
				boundaryEntity.Title,
				Path.GetRelativePath(layout.VaultRoot, newPath),
				cancellationToken);
		}
	}

	/// <summary>
	/// Determines whether an implicit entity's synchronization boundary has already begun, meaning a file for it exists
	/// or a boundary entry was previously recorded.
	/// </summary>
	private async Task<bool> IsImplicitBoundaryMaterializedAsync(object entity, string? previousPath, string? sourcePath, CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(previousPath) && File.Exists(previousPath))
		{
			return true;
		}

		if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(Path.GetFullPath(sourcePath)))
		{
			return true;
		}

		return entity is IPuckNamedEntity namedEntity
			&& !string.IsNullOrWhiteSpace(namedEntity.Id)
			&& await implicitBoundaryService.HasBoundaryBegunAsync(entity.GetType().Name, namedEntity.Id, cancellationToken);
	}

	private string ResolveInactionPreferredPath(object entity, string canonicalPath, string? sourcePath)
	{
		if (string.IsNullOrWhiteSpace(sourcePath))
		{
			return canonicalPath;
		}

		var fullSourcePath = Path.GetFullPath(sourcePath);
		if (!File.Exists(fullSourcePath)
			|| !string.Equals(Path.GetExtension(fullSourcePath), ".md", StringComparison.OrdinalIgnoreCase))
		{
			return canonicalPath;
		}

		if (entity is not Incentive incentive)
		{
			return canonicalPath;
		}

		if (!IsIncentiveSourcePlacementValid(incentive, fullSourcePath))
		{
			return canonicalPath;
		}

		var sourceDirectory = Path.GetDirectoryName(fullSourcePath);
		if (string.IsNullOrWhiteSpace(sourceDirectory))
		{
			return canonicalPath;
		}

		return Path.Combine(sourceDirectory, Path.GetFileName(canonicalPath));
	}

	private bool IsIncentiveSourcePlacementValid(Incentive incentive, string fullSourcePath)
	{
		if (string.IsNullOrWhiteSpace(incentive.DirectiveId))
		{
			return IsUnderRoot(fullSourcePath, ResolveStorageRoot(incentive.GetType()));
		}

		var containingDirectiveId = pathPolicy.TryResolveContainingDirectiveId(fullSourcePath);
		if (!string.Equals(containingDirectiveId, incentive.DirectiveId, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var storage = GetStorageAttribute(incentive.GetType());
		if (string.IsNullOrWhiteSpace(storage.PartitionUnder))
		{
			return true;
		}

		var expectedPartition = storage.PartitionUnder.Trim();
		var containingDirectory = Path.GetDirectoryName(fullSourcePath);
		var containingDirectoryName = Path.GetFileName(containingDirectory?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		return string.Equals(containingDirectoryName, expectedPartition, StringComparison.OrdinalIgnoreCase);
	}

	private string ResolveFreeformTargetPath(object entity, string defaultPath, string? sourcePath)
	{
		var storage = GetStorageAttribute(entity.GetType());
		if (storage.Mode != VaultStorageMode.Freeform || string.IsNullOrWhiteSpace(sourcePath))
		{
			return defaultPath;
		}

		var fullSourcePath = Path.GetFullPath(sourcePath);
		if (!File.Exists(fullSourcePath))
		{
			return defaultPath;
		}

		if (IsAllowedFreeformAssertion(entity.GetType(), fullSourcePath))
		{
			return fullSourcePath;
		}

		return ResolveFreeformFallbackPath(defaultPath);
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
		var fallbackFileName = fileNameWithoutExtension;
		var fallbackPath = defaultPath;
		var suffix = 2;

		while (File.Exists(fallbackPath) || Directory.Exists(fallbackDirectory))
		{
			fallbackDirectory = Path.Combine(Path.GetDirectoryName(directory) ?? directory, $"{Path.GetFileName(directory)} ({suffix})");
			fallbackFileName = $"{fileNameWithoutExtension} ({suffix})";
			fallbackPath = Path.Combine(fallbackDirectory, $"{fallbackFileName}{extension}");
			suffix++;
		}

		return fallbackPath;
	}

	private string? TryRelocateSelfNamedDirectory(string? previousPath, string newPath, Type entityType)
	{
		if (string.IsNullOrWhiteSpace(previousPath)
			|| string.Equals(previousPath, newPath, StringComparison.OrdinalIgnoreCase))
		{
			return previousPath;
		}

		var storage = GetStorageAttribute(entityType);
		if (storage.Shape != VaultStorageShape.SelfNamedDirectory)
		{
			return previousPath;
		}

		var previousDirectory = Path.GetDirectoryName(previousPath);
		var newDirectory = Path.GetDirectoryName(newPath);
		if (string.IsNullOrWhiteSpace(previousDirectory)
			|| string.IsNullOrWhiteSpace(newDirectory)
			|| string.Equals(previousDirectory, newDirectory, StringComparison.OrdinalIgnoreCase)
			|| !Directory.Exists(previousDirectory)
			|| Directory.Exists(newDirectory))
		{
			return previousPath;
		}

		Directory.CreateDirectory(Path.GetDirectoryName(newDirectory)!);
		SuppressWatcherPaths(previousDirectory, newDirectory, previousPath, newPath);
		Directory.Move(previousDirectory, newDirectory);

		var relocatedPreviousPath = Path.Combine(newDirectory, Path.GetFileName(previousPath));
		if (File.Exists(relocatedPreviousPath))
		{
			return relocatedPreviousPath;
		}

		return previousPath;
	}

	private async Task<string> ResolveLoreParentReassignmentPathAsync(LorePage lorePage, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(lorePage);

		var folderName = PuckNamedIdentity.FormatFileName(lorePage.EffectiveIdentifier, lorePage.Title);
		if (string.IsNullOrWhiteSpace(lorePage.ParentId))
		{
			if (string.Equals(lorePage.Level, "Cha", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(lorePage.Level, "Act", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(lorePage.Level, "p", StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException($"Lore page '{lorePage.Id}' cannot be reassigned vertically without a valid parent for level '{lorePage.Level}'.");
			}

			return Path.Combine(layout.SagaRoot, folderName, $"{folderName}.md");
		}

		var parent = await context.LorePages
			.AsNoTracking()
			.FirstOrDefaultAsync(item => item.Id == lorePage.ParentId, cancellationToken)
			?? throw new InvalidOperationException($"Lore parent '{lorePage.ParentId}' was not found during reassignment sync.");

		ValidateLoreParentReassignment(lorePage, parent);

		var parentPath = await ResolveCanonicalPathAsync(parent, cancellationToken);
		var parentDirectory = Path.GetDirectoryName(parentPath)
			?? throw new InvalidOperationException($"Lore parent '{parent.Id}' canonical path does not have a valid directory.");

		return Path.Combine(parentDirectory, folderName, $"{folderName}.md");
	}

	private static void ValidateLoreParentReassignment(LorePage lorePage, LorePage parent)
	{
		var expectedParentLevel = lorePage.Level.Trim().ToLowerInvariant() switch
		{
			"era" => null,
			"cha" => "Era",
			"act" => "Cha",
			"p" => "Act",
			_ => null,
		};

		if (expectedParentLevel is null)
		{
			if (string.Equals(lorePage.Level, "era", StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException($"Lore page '{lorePage.Id}' is level '{lorePage.Level}' and cannot be reassigned under parent '{parent.Id}'.");
			}

			return;
		}

		if (!string.Equals(parent.Level, expectedParentLevel, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException(
				$"Lore reassignment from '{lorePage.Id}' to parent '{parent.Id}' is vertical and not supported by the static nesting pattern. Expected parent level '{expectedParentLevel}', but found '{parent.Level}'.");
		}
	}

	private async Task<FileGraveyardEntry?> DeleteEntityPathAsync(object entity, CancellationToken cancellationToken)
	{
		var canonicalPath = await ResolveCanonicalPathAsync(entity, cancellationToken);
		var path = await TryResolveExistingPathByIdentityAsync(entity, canonicalPath, cancellationToken)
			?? canonicalPath;
		var namedEntity = entity as IPuckNamedEntity;
		return await DeletePathAsync(path, ResolveStorageRoot(entity.GetType()), entity.GetType().Name, namedEntity?.Id, namedEntity?.Title, "api-delete", cancellationToken);
	}

	private async Task<string?> TryResolveExistingPathByIdentityAsync(object entity, string? preferredPath, CancellationToken cancellationToken)
	{
		if (entity is not IPuckNamedEntity namedEntity
			|| string.IsNullOrWhiteSpace(namedEntity.Id))
		{
			return null;
		}

		var candidatePaths = EnumerateIdentityCandidatePaths(entity.GetType());
		var matches = new List<string>();
		foreach (var candidatePath in candidatePaths)
		{
			if (!File.Exists(candidatePath))
			{
				continue;
			}

			if (!await IsIdentityMatchAsync(candidatePath, namedEntity.Id, cancellationToken))
			{
				continue;
			}

			matches.Add(Path.GetFullPath(candidatePath));
		}

		if (matches.Count == 0)
		{
			return null;
		}

		if (!string.IsNullOrWhiteSpace(preferredPath))
		{
			var normalizedPreferredPath = Path.GetFullPath(preferredPath);
			var preferredMatch = matches.FirstOrDefault(path => string.Equals(path, normalizedPreferredPath, StringComparison.OrdinalIgnoreCase));
			if (!string.IsNullOrWhiteSpace(preferredMatch))
			{
				return preferredMatch;
			}
		}

		return matches
			.OrderByDescending(File.GetLastWriteTimeUtc)
			.ThenBy(path => path.Length)
			.First();
	}

	private IEnumerable<string> EnumerateIdentityCandidatePaths(Type entityType)
	{
		var model = pathSyncModelCatalog
			.GetModels()
			.FirstOrDefault(candidate => candidate.EntityType == entityType);

		if (model is not null)
		{
			return pathSyncModelCatalog.EnumerateCandidateMarkdownPaths(model);
		}

		var root = ResolveStorageRoot(entityType);
		if (!Directory.Exists(root))
		{
			return [];
		}

		return Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories);
	}

	private async Task<bool> IsIdentityMatchAsync(string markdownPath, string id, CancellationToken cancellationToken)
	{
		var parsedId = MarkdownFileLocator.ParseLoosePuckIdentityFromPath(markdownPath).Id;
		if (!string.IsNullOrWhiteSpace(parsedId)
			&& string.Equals(parsedId, id, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		var markdown = await File.ReadAllTextAsync(markdownPath, cancellationToken);
		var frontMatter = markdownSerializer.ParseFrontMatter(markdown);
		if (!frontMatter.TryGetValue("puck", out var rawPuck)
			|| string.IsNullOrWhiteSpace(rawPuck))
		{
			return false;
		}

		var normalizedPuck = rawPuck.Trim().Trim('"');
		return string.Equals(normalizedPuck, id, StringComparison.OrdinalIgnoreCase);
	}

	private async Task<string> ResolveCanonicalPathAsync(object entity, CancellationToken cancellationToken)
	{
		var parent = await LoadParentHierarchyAsync(entity, new HashSet<string>(StringComparer.OrdinalIgnoreCase), cancellationToken);
		return markdownFileLocator.GetFilePath(entity, parent);
	}

	private async Task<object?> LoadParentHierarchyAsync(object entity, HashSet<string> visited, CancellationToken cancellationToken)
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

		if (entity is IPuckNamedEntity namedEntity
			&& storage.ParentEntityType == entity.GetType()
			&& string.Equals(namedEntity.Id, parentId, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		var parentKey = $"{storage.ParentEntityType.FullName}:{parentId}";
		if (!visited.Add(parentKey))
		{
			return null;
		}

		var parent = await FindEntityAsync(storage.ParentEntityType, parentId, cancellationToken);
		if (parent is null)
		{
			return null;
		}

		var parentParent = await LoadParentHierarchyAsync(parent, visited, cancellationToken);
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

	private async Task<IReadOnlyDictionary<string, string>?> ResolveFrontMatterAsync(string? previousPath, string? sourcePath, string currentPath, CancellationToken cancellationToken)
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
			return null;
		}

		var markdown = await File.ReadAllTextAsync(candidatePath, cancellationToken);
		return markdownSerializer.ParseFrontMatter(markdown);
	}

	private static string ExtractBody(string markdown)
	{
		using var reader = new StringReader(markdown);
		var firstLine = reader.ReadLine();
		if (!string.Equals(NormalizeFrontMatterDelimiterLine(firstLine), "---", StringComparison.Ordinal))
		{
			return markdown;
		}

		while (reader.ReadLine() is { } line)
		{
			if (line == "---")
			{
				break;
			}
		}

		return reader.ReadToEnd();
	}

	private static string? NormalizeFrontMatterDelimiterLine(string? line)
	{
		if (line is null)
		{
			return null;
		}

		if (line.Length > 0 && line[0] == '\uFEFF')
		{
			line = line[1..];
		}

		return line.Trim();
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
			layout.FatesRoot,
			layout.DecreesRoot,
			layout.OnrushRoot,
			layout.JournalRoot,
			layout.SagaRoot,
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
