using System.Reflection;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Saga;
using Pleiades.Vault;

namespace Pleiades.Vault.Markdown;

/// <summary>
/// Resolves canonical markdown file paths for vault-backed entities.
/// </summary>
public sealed class MarkdownFileLocator(VaultStoragePathComposer composer)
{
	private readonly VaultStoragePathComposer _composer = composer;

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
	/// <remarks>
	/// Path composition is driven entirely by the entity's declared <see cref="VaultStorageAttribute"/> policy through
	/// per-shape <see cref="IVaultStorageStrategy"/> objects (see <see cref="VaultStoragePathComposer"/>), rather than
	/// per-type dispatch.
	/// </remarks>
	public string GetFilePath(object entity, object? parentEntity)
	{
		ArgumentNullException.ThrowIfNull(entity);
		if (!HasFileBacking(entity.GetType()))
		{
			throw new InvalidOperationException($"Type '{entity.GetType().Name}' is not configured for vault markdown storage.");
		}

		return _composer.GetFilePath(entity, parentEntity);
	}

	/// <summary>
	/// Resolves the directory path for a directive (its self-named storage folder).
	/// </summary>
	public string GetDirectiveDirectoryPath(Directive directive, Directive? parentDirective = null)
	{
		ArgumentNullException.ThrowIfNull(directive);
		return _composer.GetOwnDirectory(directive, parentDirective);
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
	/// Resolves the markdown file path for a directive with an optional resolved parent directive.
	/// </summary>
	public string GetDirectiveFilePath(Directive directive, Directive? parentDirective)
	{
		ArgumentNullException.ThrowIfNull(directive);
		return GetFilePath(directive, parentDirective);
	}

	/// <summary>
	/// Resolves the markdown file path for an objective.
	/// </summary>
	public string GetObjectiveFilePath(Objective objective)
	{
		return GetObjectiveFilePath(objective, null);
	}

	/// <summary>
	/// Resolves the markdown file path for an objective with an optional owning directive.
	/// </summary>
	public string GetObjectiveFilePath(Objective objective, Directive? owningDirective)
	{
		ArgumentNullException.ThrowIfNull(objective);
		return GetFilePath(objective, owningDirective);
	}

	/// <summary>
	/// Resolves the markdown file path for an onrush sprint.
	/// </summary>
	/// <param name="sprint">The sprint whose markdown path should be resolved.</param>
	/// <returns>The absolute markdown file path.</returns>
	public string GetOnrushSprintFilePath(OnrushSprint sprint)
	{
		ArgumentNullException.ThrowIfNull(sprint);
		return GetFilePath(sprint, null);
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
		return GetFilePath(order, owningSprint);
	}

	/// <summary>
	/// Resolves the markdown file path for a Polaris cycle.
	/// </summary>
	/// <param name="cycle">The cycle whose markdown path should be resolved.</param>
	/// <returns>The absolute markdown file path.</returns>
	public string GetPolarisCycleFilePath(PolarisCycle cycle)
	{
		ArgumentNullException.ThrowIfNull(cycle);
		return GetFilePath(cycle, null);
	}

	/// <summary>
	/// Resolves the markdown file path for a lore page.
	/// </summary>
	public string GetLorePageFilePath(LorePage lorePage)
	{
		ArgumentNullException.ThrowIfNull(lorePage);
		return GetFilePath(lorePage, null);
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
	/// Resolves a directive's frontmatter identity from a directory (its self-named primary file, or any markdown file
	/// in it). The nearest-containing-directive <em>walk-up</em> lives in one place — <see cref="VaultWatcherPathPolicy"/>
	/// — so ownership-boundary enforcement is defined once; this is only the per-directory leaf it calls.
	/// </summary>
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

		// A directive's identity is Quiet — it lives in frontmatter, never in the filename. Loose-parsing the filename
		// would read a dashed *title* ("2024 - Roadmap") as a phantom prefix ("2024") and orphan any child that resolves
		// its parent here; the frontmatter PUCK is the only trustworthy directive identity (.GENESIS principle 1).
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

}