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
			Fate fate => GetIncentiveFilePath(fate, layout.FatesRoot, parentEntity as Directive),
			Decree decree => GetIncentiveFilePath(decree, layout.DecreesRoot, parentEntity as Directive),
			OnrushSprint sprint => GetOnrushSprintFilePath(sprint),
			ExecutiveOrder order => GetExecutiveOrderFilePath(order, parentEntity as OnrushSprint),
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
		var storage = ResolveDirectiveStorage(directive.GetType());
		var folderName = ResolvePuckFileBaseName(storage, directive.Id, directive.Title);
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
		var storage = ResolveDirectiveStorage(directive.GetType());
		var folderName = ResolvePuckFileBaseName(storage, directive.Id, directive.Title);
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
		var storage = typeof(Objective).GetCustomAttribute<VaultStorageAttribute>()
			?? throw new InvalidOperationException($"Type '{typeof(Objective).Name}' is not configured for vault markdown storage.");
		var fileName = ResolvePuckFileBaseName(storage, objective.Id, objective.Title);
		var container = owningDirective is null
			? layout.ObjectivesRoot
			: ResolvePartitionedParentDirectory(typeof(Objective), GetDirectiveDirectoryPath(owningDirective, owningDirective.ParentDirective));

		return Path.Combine(container, $"{fileName}.md");
	}

	/// <summary>
	/// Resolves the markdown file path for a declarative incentive (fate or decree), which follows the
	/// objective placement policy: its standalone root, or its partition folder inside the owning directive.
	/// </summary>
	private string GetIncentiveFilePath(Incentive incentive, string standaloneRoot, Directive? owningDirective)
	{
		var storage = incentive.GetType().GetCustomAttribute<VaultStorageAttribute>()
			?? throw new InvalidOperationException($"Type '{incentive.GetType().Name}' is not configured for vault markdown storage.");
		var fileName = ResolvePuckFileBaseName(storage, incentive.Id, incentive.Title);
		var container = owningDirective is null
			? standaloneRoot
			: ResolvePartitionedParentDirectory(incentive.GetType(), GetDirectiveDirectoryPath(owningDirective, owningDirective.ParentDirective));

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
	/// Resolves the markdown file path for an executive order inside its owning onrush sprint's partition folder.
	/// </summary>
	/// <param name="order">The executive order whose markdown path should be resolved.</param>
	/// <param name="owningSprint">The resolved owning onrush sprint.</param>
	/// <returns>The absolute markdown file path.</returns>
	public string GetExecutiveOrderFilePath(ExecutiveOrder order, OnrushSprint? owningSprint)
	{
		ArgumentNullException.ThrowIfNull(order);
		EnsureFileBacking<ExecutiveOrder>();
		if (owningSprint is null)
		{
			throw new InvalidOperationException($"Executive order '{order.Id}' cannot compose a storage path without its owning onrush sprint '{order.OnrushSprintId}'.");
		}

		var fileName = PuckNamedIdentity.FormatFileName(order.Id, order.Title);
		var sprintDirectory = Path.GetDirectoryName(GetOnrushSprintFilePath(owningSprint))
			?? throw new InvalidOperationException($"Onrush sprint '{owningSprint.Id}' markdown path does not have a valid directory.");
		var container = ResolvePartitionedParentDirectory(typeof(ExecutiveOrder), sprintDirectory);
		return Path.Combine(container, $"{fileName}.md");
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
		ApplyIncentiveCompositionFromPath(objective, path);
	}

	/// <summary>
	/// Derives incentive identity and owning directive relation from a canonical markdown path.
	/// Applies to all incentive kinds: objectives and the fate/decree declaratives (PEP100).
	/// </summary>
	public static void ApplyIncentiveCompositionFromPath(Incentive incentive, string path)
	{
		ArgumentNullException.ThrowIfNull(incentive);
		ApplyLoosePuckIdentityFromPath(incentive, path);
		incentive.DirectiveId = TryGetContainingDirectiveId(path, skipCurrentIfSelfNamed: false);
	}

	/// <summary>
	/// Derives executive order identity and owning onrush sprint relation from a canonical markdown path.
	/// </summary>
	public static void ApplyExecutiveOrderCompositionFromPath(ExecutiveOrder order, string path)
	{
		ArgumentNullException.ThrowIfNull(order);
		ApplyLoosePuckIdentityFromPath(order, path);
		var containingSprintId = TryGetContainingOnrushSprintId(path);
		if (!string.IsNullOrWhiteSpace(containingSprintId))
		{
			order.OnrushSprintId = containingSprintId;
		}
	}

	/// <summary>
	/// Tries to resolve the owning onrush sprint identifier for an executive order markdown path.
	/// The path must sit inside the order partition folder of a self-named onrush sprint directory.
	/// </summary>
	public static string? TryGetContainingOnrushSprintId(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}

		var partition = typeof(ExecutiveOrder).GetCustomAttribute<VaultStorageAttribute>()?.PartitionUnder?.Trim();
		var partitionDirectory = Path.GetDirectoryName(path);
		if (string.IsNullOrWhiteSpace(partition)
			|| string.IsNullOrWhiteSpace(partitionDirectory)
			|| !string.Equals(Path.GetFileName(partitionDirectory), partition, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		var sprintDirectory = Directory.GetParent(partitionDirectory)?.FullName;
		if (string.IsNullOrWhiteSpace(sprintDirectory) || !IsSelfNamedDirectory(sprintDirectory))
		{
			return null;
		}

		return PuckNamedIdentity.ParseLoose(Path.GetFileName(sprintDirectory)).Id;
	}

	/// <summary>
	/// Derives lore page composition from a canonical saga markdown path.
	/// </summary>
	public static bool ApplyLorePageCompositionFromPath(LorePage lorePage, string path, string vaultRoot, string sagaRoot, ILogger? logger = null)
	{
		return LorePage.TryApplyCompositionFromPath(lorePage, path, vaultRoot, sagaRoot, logger);
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
			var resolved = TryResolveDirectivePuckFromDirectory(currentDirectory);
			if (!string.IsNullOrWhiteSpace(resolved))
			{
				return resolved;
			}

			currentDirectory = Directory.GetParent(currentDirectory)?.FullName;
		}

		return null;
	}

	public static string? TryResolveDirectivePuckFromDirectory(string? directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
		{
			return null;
		}

		if (IsSelfNamedDirectory(directoryPath))
		{
			var selfNamedPrimary = Path.Combine(directoryPath, $"{Path.GetFileName(directoryPath)}.md");
			var fromSelfNamed = TryResolveDirectivePuckFromMarkdownFile(selfNamedPrimary);
			if (!string.IsNullOrWhiteSpace(fromSelfNamed))
			{
				return fromSelfNamed;
			}
		}

		foreach (var markdownFile in Directory.EnumerateFiles(directoryPath, "*.md", SearchOption.TopDirectoryOnly)
			.OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
		{
			var resolved = TryResolveDirectivePuckFromMarkdownFile(markdownFile);
			if (!string.IsNullOrWhiteSpace(resolved))
			{
				return resolved;
			}
		}

		return null;
	}

	private static string? TryResolveDirectivePuckFromMarkdownFile(string markdownPath)
	{
		if (!File.Exists(markdownPath))
		{
			return null;
		}

		var parsed = ParseLoosePuckIdentityFromPath(markdownPath).Id;
		if (!string.IsNullOrWhiteSpace(parsed))
		{
			return parsed;
		}

		return TryReadFrontMatterPuck(markdownPath);
	}

	private static string? TryReadFrontMatterPuck(string markdownPath)
	{
		if (!File.Exists(markdownPath))
		{
			return null;
		}

		using var reader = new StreamReader(markdownPath);
		var firstLine = reader.ReadLine();
		if (!string.Equals(firstLine?.TrimStart('\uFEFF').Trim(), "---", StringComparison.Ordinal))
		{
			return null;
		}

		while (reader.ReadLine() is { } line)
		{
			if (string.Equals(line.Trim(), "---", StringComparison.Ordinal))
			{
				break;
			}

			var separatorIndex = line.IndexOf(':');
			if (separatorIndex <= 0)
			{
				continue;
			}

			var key = line[..separatorIndex].Trim();
			if (!string.Equals(key, "puck", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var rawValue = line[(separatorIndex + 1)..].Trim().Trim('"');
			return string.IsNullOrWhiteSpace(rawValue)
				? null
				: rawValue;
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

	/// <summary>
	/// Resolves the primary file base name for a PUCK-backed entity according to its declared PUCK storage form.
	/// Quiet storage yields a title-only name, while Index storage embeds the PUCK token in the filename.
	/// </summary>
	private static string ResolvePuckFileBaseName(VaultStorageAttribute storage, string id, string title)
	{
		return storage.PuckStorage == VaultPuckStorage.Quiet
			? PuckNamedIdentity.FormatTitleOnlyFileName(title)
			: PuckNamedIdentity.FormatFileName(id, title);
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
			return ResolvePartitionedParentDirectory(directive.GetType(), GetDirectiveDirectoryPath(parentDirective, parentDirective.ParentDirective));
		}

		// Root directives land in the location declared by their concrete type: stellar under Directives, lunar
		// under its dedicated Moonlight root (PEP100).
		return layout.GetLocationRoot(ResolveDirectiveStorage(directive.GetType()).LocationKey);
	}

	private static VaultStorageAttribute ResolveDirectiveStorage(Type directiveType)
	{
		return directiveType.GetCustomAttribute<VaultStorageAttribute>(inherit: true)
			?? throw new InvalidOperationException($"Type '{directiveType.Name}' is not configured for vault markdown storage.");
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