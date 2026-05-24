using System.Reflection;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Saga;
using Pleiades.Vault;

namespace Pleiades.Vault.Markdown;

/// <summary>
/// Resolves canonical markdown file paths for vault-backed entities.
/// </summary>
public sealed class MarkdownFileLocator(VaultLayout layout)
{
	/// <summary>
	/// Determines whether a type has explicit vault file backing.
	/// </summary>
	/// <param name="entityType">The type to inspect.</param>
	/// <returns><see langword="true"/> when the type is vault-backed; otherwise, <see langword="false"/>.</returns>
	public static bool HasFileBacking(Type entityType)
	{
		ArgumentNullException.ThrowIfNull(entityType);
		return entityType.GetCustomAttribute<VaultStorageAttribute>() is not null;
	}

	/// <summary>
	/// Resolves the markdown file path for a vault-backed entity.
	/// </summary>
	/// <param name="entity">The entity whose markdown path should be resolved.</param>
	/// <returns>The absolute markdown file path.</returns>
	public string GetFilePath(object entity)
	{
		return GetFilePath(entity, null);
	}

	/// <summary>
	/// Resolves the markdown file path for a vault-backed entity with an optional resolved parent entity.
	/// </summary>
	public string GetFilePath(object entity, object? parentEntity)
	{
		ArgumentNullException.ThrowIfNull(entity);

		return entity switch
		{
			Directive directive => GetDirectiveFilePath(directive, parentEntity as Directive),
			Objective objective => GetObjectiveFilePath(objective, parentEntity as Directive),
			OnrushSprint sprint => GetOnrushSprintFilePath(sprint),
			PolarisCycle cycle => GetPolarisCycleFilePath(cycle),
			LorePage lorePage => GetLorePageFilePath(lorePage),
			_ => throw new InvalidOperationException($"Type '{entity.GetType().Name}' is not configured for vault markdown storage."),
		};
	}

	/// <summary>
	/// Resolves the directory path for a directive.
	/// </summary>
	public string GetDirectiveDirectoryPath(Directive directive, Directive? parentDirective = null)
	{
		ArgumentNullException.ThrowIfNull(directive);
		EnsureFileBacking<Directive>();
		var storage = typeof(Directive).GetCustomAttribute<VaultStorageAttribute>()
			?? throw new InvalidOperationException($"Type '{typeof(Directive).Name}' is not configured for vault markdown storage.");
		var folderName = storage.Mode == VaultStorageMode.Freeform
			? PuckNamedIdentity.FormatTitleOnlyFileName(directive.Title)
			: PuckNamedIdentity.FormatFileName(directive.Id, directive.Title);
		var parentDirectory = ResolveDirectiveParentDirectory(directive, parentDirective);
		return Path.Combine(parentDirectory, folderName);
	}

	/// <summary>
	/// Resolves the markdown file path for a directive.
	/// </summary>
	/// <param name="directive">The directive whose markdown path should be resolved.</param>
	/// <returns>The absolute markdown file path.</returns>
	public string GetDirectiveFilePath(Directive directive)
	{
		return GetDirectiveFilePath(directive, null);
	}

	/// <summary>
	/// Resolves the markdown file path for a directive.
	/// </summary>
	public string GetDirectiveFilePath(Directive directive, Directive? parentDirective)
	{
		ArgumentNullException.ThrowIfNull(directive);
		var storage = typeof(Directive).GetCustomAttribute<VaultStorageAttribute>()
			?? throw new InvalidOperationException($"Type '{typeof(Directive).Name}' is not configured for vault markdown storage.");
		var folderName = storage.Mode == VaultStorageMode.Freeform
			? PuckNamedIdentity.FormatTitleOnlyFileName(directive.Title)
			: PuckNamedIdentity.FormatFileName(directive.Id, directive.Title);
		var directory = GetDirectiveDirectoryPath(directive, parentDirective);
		return Path.Combine(directory, $"{folderName}.md");
	}

	/// <summary>
	/// Resolves the markdown file path for an objective.
	/// </summary>
	public string GetObjectiveFilePath(Objective objective)
	{
		return GetObjectiveFilePath(objective, null);
	}

	/// <summary>
	/// Resolves the markdown file path for an objective.
	/// </summary>
	public string GetObjectiveFilePath(Objective objective, Directive? owningDirective)
	{
		ArgumentNullException.ThrowIfNull(objective);
		EnsureFileBacking<Objective>();
		var fileName = PuckNamedIdentity.FormatFileName(objective.Id, objective.Title);
		var container = owningDirective is null
			? layout.ObjectivesRoot
			: ResolvePartitionedParentDirectory(typeof(Objective), GetDirectiveDirectoryPath(owningDirective, owningDirective.ParentDirective));

		return Path.Combine(container, $"{fileName}.md");
	}

	/// <summary>
	/// Resolves the markdown file path for an onrush sprint.
	/// </summary>
	/// <param name="sprint">The sprint whose markdown path should be resolved.</param>
	/// <returns>The absolute markdown file path.</returns>
	public string GetOnrushSprintFilePath(OnrushSprint sprint)
	{
		ArgumentNullException.ThrowIfNull(sprint);
		EnsureFileBacking<OnrushSprint>();
		var folderName = PuckNamedIdentity.FormatFileName(sprint.Id, sprint.Title);
		return Path.Combine(layout.GetLocationRoot(VaultLocationKeys.Onrush), folderName, $"{folderName}.md");
	}

	/// <summary>
	/// Resolves the markdown file path for a Polaris cycle.
	/// </summary>
	/// <param name="cycle">The cycle whose markdown path should be resolved.</param>
	/// <returns>The absolute markdown file path.</returns>
	public string GetPolarisCycleFilePath(PolarisCycle cycle)
	{
		ArgumentNullException.ThrowIfNull(cycle);
		EnsureFileBacking<PolarisCycle>();
		var fileName = PuckNamedIdentity.FormatFileName(cycle.Id, cycle.Title);
		return Path.Combine(layout.GetLocationRoot(VaultLocationKeys.Journal), $"{fileName}.md");
	}

	/// <summary>
	/// Resolves the markdown file path for a lore page.
	/// </summary>
	public string GetLorePageFilePath(LorePage lorePage)
	{
		ArgumentNullException.ThrowIfNull(lorePage);
		EnsureFileBacking<LorePage>();

		if (!string.IsNullOrWhiteSpace(lorePage.RelativePath))
		{
			var path = Path.GetFullPath(Path.Combine(layout.VaultRoot, lorePage.RelativePath));
			var normalizedRoot = layout.VaultRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			if (path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
			{
				return path;
			}
		}

		var folderName = PuckNamedIdentity.FormatFileName(lorePage.EffectiveIdentifier, lorePage.Title);
		return Path.Combine(layout.SagaRoot, folderName, $"{folderName}.md");
	}

	/// <summary>
	/// Parses a PUCK identity from a markdown path using the "{Id} - {Title}" filename convention.
	/// </summary>
	/// <param name="path">The markdown file path.</param>
	/// <returns>The parsed PUCK token and title.</returns>
	public static (string Id, string Title) ParsePuckIdentityFromPath(string path)
	{
		try
		{
			return PuckNamedIdentity.ParsePath(path);
		}
		catch (FormatException exception)
		{
			throw new InvalidOperationException($"Path '{path}' does not follow the expected '{{PUCK token}} - {{Title}}.md' naming convention.", exception);
		}
	}

	/// <summary>
	/// Parses a markdown path using either the canonical <c>{PUCK token} - {Title}</c> format or a title-only filename.
	/// </summary>
	/// <param name="path">The markdown file path.</param>
	/// <returns>The parsed optional PUCK token and title.</returns>
	public static (string? Id, string Title) ParseLoosePuckIdentityFromPath(string path)
	{
		return PuckNamedIdentity.ParseLoosePath(path);
	}

	/// <summary>
	/// Applies a PUCK identity parsed from a markdown path to a target entity.
	/// </summary>
	/// <typeparam name="T">The entity type.</typeparam>
	/// <param name="entity">The entity to populate.</param>
	/// <param name="path">The markdown file path.</param>
	public static void ApplyPuckIdentityFromPath<T>(T entity, string path)
		where T : IPuckNamedEntity
	{
		ArgumentNullException.ThrowIfNull(entity);
		var fileName = Path.GetFileNameWithoutExtension(path);
		PuckNamedIdentity.ApplyTo(entity, fileName);
	}

	/// <summary>
	/// Applies a loose markdown identity to a target entity, preserving its PUCK token when the file is title-only.
	/// </summary>
	/// <typeparam name="T">The entity type.</typeparam>
	/// <param name="entity">The entity to populate.</param>
	/// <param name="path">The markdown file path.</param>
	public static void ApplyLoosePuckIdentityFromPath<T>(T entity, string path)
		where T : IPuckNamedEntity
	{
		ArgumentNullException.ThrowIfNull(entity);
		var (id, title) = PuckNamedIdentity.ParseLoosePath(path);
		if (!string.IsNullOrWhiteSpace(id))
		{
			entity.Id = id;
		}

		entity.Title = title;
	}

	/// <summary>
	/// Derives directive identity and parent relation from a canonical markdown path.
	/// </summary>
	public static void ApplyDirectiveCompositionFromPath(Directive directive, string path)
	{
		ArgumentNullException.ThrowIfNull(directive);
		ApplyLoosePuckIdentityFromPath(directive, path);
		directive.ParentDirectiveId = TryGetContainingDirectiveId(Path.GetDirectoryName(path), skipCurrentIfSelfNamed: true);
	}

	/// <summary>
	/// Derives objective identity and owning directive relation from a canonical markdown path.
	/// </summary>
	public static void ApplyObjectiveCompositionFromPath(Objective objective, string path)
	{
		ArgumentNullException.ThrowIfNull(objective);
		ApplyLoosePuckIdentityFromPath(objective, path);
		objective.DirectiveId = TryGetContainingDirectiveId(path, skipCurrentIfSelfNamed: false);
	}

	/// <summary>
	/// Derives lore page composition from a canonical saga markdown path.
	/// </summary>
	public static bool ApplyLorePageCompositionFromPath(LorePage lorePage, string path, string vaultRoot, string sagaRoot)
	{
		return LorePage.TryApplyCompositionFromPath(lorePage, path, vaultRoot, sagaRoot);
	}

	/// <summary>
	/// Tries to resolve the nearest containing directive identifier from a markdown path.
	/// </summary>
	public static string? TryGetContainingDirectiveId(string? path, bool skipCurrentIfSelfNamed = false)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}

		var currentDirectory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
		if (skipCurrentIfSelfNamed && !string.IsNullOrWhiteSpace(currentDirectory) && IsSelfNamedDirectory(currentDirectory))
		{
			currentDirectory = Directory.GetParent(currentDirectory)?.FullName;
		}

		while (!string.IsNullOrWhiteSpace(currentDirectory))
		{
			if (IsSelfNamedDirectory(currentDirectory))
			{
				var primaryFile = Path.Combine(currentDirectory, $"{Path.GetFileName(currentDirectory)}.md");
				return File.Exists(primaryFile)
					? ParseLoosePuckIdentityFromPath(primaryFile).Id
					: null;
			}

			currentDirectory = Directory.GetParent(currentDirectory)?.FullName;
		}

		return null;
	}

	/// <summary>
	/// Determines whether a markdown path is the primary markdown file for a self-named storage directory.
	/// </summary>
	public static bool IsPrimarySelfNamedFile(string path)
	{
		return string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Determines whether a directory is a self-named storage directory.
	/// </summary>
	public static bool IsSelfNamedDirectory(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
		{
			return false;
		}

		var directoryName = Path.GetFileName(directoryPath);
		return File.Exists(Path.Combine(directoryPath, $"{directoryName}.md"));
	}

	private static void EnsureFileBacking<T>()
	{
		if (!HasFileBacking(typeof(T)))
		{
			throw new InvalidOperationException($"Type '{typeof(T).Name}' is not configured for vault markdown storage.");
		}
	}

	private string ResolveDirectiveParentDirectory(Directive directive, Directive? parentDirective)
	{
		if (parentDirective is not null)
		{
			return ResolvePartitionedParentDirectory(typeof(Directive), GetDirectiveDirectoryPath(parentDirective, parentDirective.ParentDirective));
		}

		return layout.GetLocationRoot(VaultLocationKeys.Directives);
	}

	private string ResolvePartitionedParentDirectory(Type entityType, string parentDirectory)
	{
		var storage = entityType.GetCustomAttribute<VaultStorageAttribute>()
			?? throw new InvalidOperationException($"Type '{entityType.Name}' is not configured for vault markdown storage.");

		if (string.IsNullOrWhiteSpace(storage.PartitionUnder))
		{
			return parentDirectory;
		}

		var partition = storage.PartitionUnder.Trim();
		if (Path.IsPathRooted(partition)
			|| partition.Contains(Path.DirectorySeparatorChar)
			|| partition.Contains(Path.AltDirectorySeparatorChar)
			|| partition.Contains("..", StringComparison.Ordinal))
		{
			throw new InvalidOperationException($"{nameof(VaultStorageAttribute.PartitionUnder)} for '{entityType.Name}' must be a single safe subdirectory name.");
		}

		return Path.Combine(parentDirectory, partition);
	}

}