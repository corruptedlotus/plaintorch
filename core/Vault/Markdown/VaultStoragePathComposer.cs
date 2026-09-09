using System.Reflection;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Saga;

namespace Pleiades.Vault.Markdown;

/// <summary>
/// Composes canonical vault paths for one storage <see cref="VaultStorageShape"/>, parameterised entirely by an
/// entity's declared <see cref="VaultStorageAttribute"/> policy. Strategies are stateless; the shared base-name,
/// container, and parent-hierarchy rules live on the <see cref="VaultStoragePathComposer"/> that hosts them, which
/// each strategy receives so it can recurse into a parent's own strategy.
/// </summary>
public interface IVaultStorageStrategy
{
	/// <summary>Gets the storage shape this strategy composes.</summary>
	VaultStorageShape Shape { get; }

	/// <summary>Resolves the canonical markdown file path for an entity, given an optional resolved immediate parent.</summary>
	string GetFilePath(VaultStoragePathComposer composer, object entity, object? immediateParent);

	/// <summary>
	/// Resolves the directory that represents the entity itself: a self-named entity's own folder, or the container a
	/// single-file entity's file sits in. Used when composing a child's path beneath this entity as a parent.
	/// </summary>
	string GetOwnDirectory(VaultStoragePathComposer composer, object entity, object? immediateParent);
}

/// <summary>
/// Resolves canonical vault paths from declared <see cref="VaultStorageAttribute"/> policy (via
/// <see cref="VaultEntityModelCatalog"/>) rather than per-entity dispatch. Shape variety is handled by
/// <see cref="IVaultStorageStrategy"/> implementations, one per <see cref="VaultStorageShape"/>, with genuinely
/// per-model placement (lore pages) handled by an explicit type override rather than a new dispatch arm.
/// </summary>
public sealed class VaultStoragePathComposer
{
	private readonly VaultEntityModelCatalog _catalog;
	private readonly IReadOnlyDictionary<VaultStorageShape, IVaultStorageStrategy> _shapeStrategies;
	private readonly IReadOnlyDictionary<Type, IVaultStorageStrategy> _typeOverrides;

	/// <summary>
	/// Initializes the composer.
	/// </summary>
	/// <param name="layout">The active vault layout used to resolve location roots.</param>
	/// <param name="catalog">The entity model catalog supplying each type's effective storage policy.</param>
	public VaultStoragePathComposer(VaultLayout layout, VaultEntityModelCatalog catalog)
	{
		Layout = layout ?? throw new ArgumentNullException(nameof(layout));
		_catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
		_shapeStrategies = new Dictionary<VaultStorageShape, IVaultStorageStrategy>
		{
			[VaultStorageShape.SelfNamedDirectory] = new SelfNamedDirectoryStorageStrategy(),
			[VaultStorageShape.SingleFile] = new SingleFileStorageStrategy(),
		};
		_typeOverrides = new Dictionary<Type, IVaultStorageStrategy>
		{
			// Lore pages are file-first and RelativePath-authoritative, and name their folder from the terminal PUCK
			// segment (EffectiveIdentifier) rather than the whole id — a genuine per-model placement, kept as an
			// explicit override rather than a dispatch arm (see REFACTOR Alpha phase 2).
			[typeof(LorePage)] = new LorePageStorageStrategy(),
		};
	}

	/// <summary>Gets the active vault layout.</summary>
	public VaultLayout Layout { get; }

	/// <summary>Resolves the canonical markdown file path for an entity and an optional resolved immediate parent.</summary>
	public string GetFilePath(object entity, object? immediateParent)
	{
		ArgumentNullException.ThrowIfNull(entity);
		return ResolveStrategy(entity.GetType()).GetFilePath(this, entity, immediateParent);
	}

	/// <summary>Resolves the directory that represents an entity itself (its self-named folder, or its file's container).</summary>
	public string GetOwnDirectory(object entity, object? immediateParent)
	{
		ArgumentNullException.ThrowIfNull(entity);
		return ResolveStrategy(entity.GetType()).GetOwnDirectory(this, entity, immediateParent);
	}

	/// <summary>
	/// Applies path-derived identity and parent relation to a freshly-constructed entity — the reverse of
	/// <see cref="GetFilePath(object, object?)"/>. Identity is parsed loosely from the path (its filename token, or
	/// lore-segment composition); the parent relation is set from the entity's declared parent policy, delegating
	/// the containing-owner resolution to the existing path helpers.
	/// </summary>
	/// <remarks>
	/// The containing-owner resolvers are genuinely per-parent-type filesystem scans (currently triplicated across
	/// <see cref="MarkdownFileLocator"/>, the path-sync catalog, and the watcher path policy). The composer selects
	/// one by the declared parent type and stays behaviour-preserving; consolidating them is REFACTOR Alpha phase 4.
	/// </remarks>
	public void ApplyCompositionFromPath(object entity, string path)
	{
		ArgumentNullException.ThrowIfNull(entity);
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		if (entity is LorePage lorePage)
		{
			MarkdownFileLocator.ApplyLorePageCompositionFromPath(lorePage, path, Layout.VaultRoot, Layout.SagaRoot);
			return;
		}

		if (entity is IPuckNamedEntity named)
		{
			ApplyFilenameIdentity(named, path);
			ApplyParentFromPath(entity, path);
		}
	}

	/// <summary>
	/// Reads a PUCK-named entity's identity from its filename, symmetric with <see cref="GetBaseName"/> and driven by the
	/// entity's declared PUCK storage form. Only <see cref="VaultPuckStorage.Index"/> storage embeds an identity token in
	/// the filename (<c>{token} - {title}</c>); there the token is loose-parsed (its notation is gated downstream, where
	/// the discovery pipeline validates it). For <see cref="VaultPuckStorage.Quiet"/> storage the PUCK lives in
	/// frontmatter and the <em>whole</em> filename is the title — there is no token to split off — so a legitimately
	/// dashed title (e.g. "Q1 - Ship it") is kept intact rather than mis-split, which would otherwise purge its
	/// "prefix - " as a phantom token when the entity's title-only file is next rewritten (.GENESIS principle 1: a raw
	/// " - " split is not an identity).
	/// </summary>
	private void ApplyFilenameIdentity(IPuckNamedEntity named, string path)
	{
		if (GetStorage(named.GetType()).PuckStorage == VaultPuckStorage.Index)
		{
			MarkdownFileLocator.ApplyLoosePuckIdentityFromPath(named, path);
			return;
		}

		named.Title = Path.GetFileNameWithoutExtension(path).Trim();
	}

	private void ApplyParentFromPath(object entity, string path)
	{
		var storage = GetStorage(entity.GetType());
		if (string.IsNullOrWhiteSpace(storage.ParentIdProperty) || storage.ParentEntityType is null)
		{
			return;
		}

		var parentId = storage.ParentEntityType == typeof(OnrushSprint)
			? MarkdownFileLocator.TryGetContainingOnrushSprintId(path)
			: storage.ParentEntityType == typeof(Directive)
				? MarkdownFileLocator.TryGetContainingDirectiveId(path, skipCurrentIfSelfNamed: storage.Shape == VaultStorageShape.SelfNamedDirectory)
				: null;

		if (!string.IsNullOrWhiteSpace(parentId))
		{
			entity.GetType()
				.GetProperty(storage.ParentIdProperty, BindingFlags.Public | BindingFlags.Instance)
				?.SetValue(entity, parentId);
		}
	}

	/// <summary>
	/// Resolves the container the entity's file or self-named folder sits directly inside: beneath a resolved parent's
	/// own directory (optionally partitioned), otherwise the entity's declared location root.
	/// </summary>
	public string GetContainer(object entity, object? immediateParent)
	{
		var storage = GetStorage(entity.GetType());
		if (immediateParent is not null)
		{
			var parentDirectory = GetOwnDirectory(immediateParent, ResolveDeclaredParent(immediateParent));
			return ApplyPartition(parentDirectory, storage, entity.GetType());
		}

		if (storage.RequiresParent)
		{
			throw new InvalidOperationException(
				$"'{entity.GetType().Name}' requires a resolved parent entity to compose its storage path.");
		}

		return Layout.GetLocationRoot(storage.LocationKey);
	}

	/// <summary>
	/// Resolves the file base name for an entity according to its declared PUCK storage form: the id-embedded
	/// <c>{token} - {title}</c> for <see cref="VaultPuckStorage.Index"/>, or a title-only name for
	/// <see cref="VaultPuckStorage.Quiet"/>.
	/// </summary>
	public string GetBaseName(object entity)
	{
		var storage = GetStorage(entity.GetType());
		var named = entity as IPuckNamedEntity
			?? throw new InvalidOperationException($"Type '{entity.GetType().Name}' is not PUCK-named and cannot compose a file name.");

		return storage.PuckStorage == VaultPuckStorage.Index
			? PuckNamedIdentity.FormatFileName(named.Id, named.Title)
			: PuckNamedIdentity.FormatTitleOnlyFileName(named.Title);
	}

	/// <summary>Resolves the effective vault storage policy for a type through the entity model catalog.</summary>
	public VaultStorageAttribute GetStorage(Type entityType)
	{
		ArgumentNullException.ThrowIfNull(entityType);
		return _catalog.GetRequired(entityType).Storage
			?? throw new InvalidOperationException($"Type '{entityType.Name}' is not configured for vault markdown storage.");
	}

	/// <summary>
	/// Resolves the parent entity referenced by an entity's declared parent navigation, used when composing paths up
	/// the ancestor chain. Returns <see langword="null"/> when the entity declares no parent or the navigation is unset.
	/// </summary>
	public object? ResolveDeclaredParent(object entity)
	{
		var storage = GetStorage(entity.GetType());
		if (string.IsNullOrWhiteSpace(storage.ParentIdProperty) || storage.ParentEntityType is null)
		{
			return null;
		}

		var navigationName = storage.ParentIdProperty.EndsWith("Id", StringComparison.Ordinal)
			? storage.ParentIdProperty[..^2]
			: storage.ParentEntityType.Name;

		return entity.GetType()
			.GetProperty(navigationName, BindingFlags.Public | BindingFlags.Instance)
			?.GetValue(entity);
	}

	private IVaultStorageStrategy ResolveStrategy(Type entityType)
	{
		if (_typeOverrides.TryGetValue(entityType, out var overrideStrategy))
		{
			return overrideStrategy;
		}

		var shape = GetStorage(entityType).Shape;
		return _shapeStrategies.TryGetValue(shape, out var strategy)
			? strategy
			: throw new InvalidOperationException($"No storage strategy is registered for shape '{shape}'.");
	}

	private static string ApplyPartition(string parentDirectory, VaultStorageAttribute storage, Type entityType)
	{
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

/// <summary>
/// Composes a self-named-directory entity: a <c>{container}/{baseName}/{baseName}.md</c> layout where the entity
/// owns a directory named after it that holds a same-named markdown file.
/// </summary>
internal sealed class SelfNamedDirectoryStorageStrategy : IVaultStorageStrategy
{
	public VaultStorageShape Shape => VaultStorageShape.SelfNamedDirectory;

	public string GetOwnDirectory(VaultStoragePathComposer composer, object entity, object? immediateParent)
		=> Path.Combine(composer.GetContainer(entity, immediateParent), composer.GetBaseName(entity));

	public string GetFilePath(VaultStoragePathComposer composer, object entity, object? immediateParent)
		=> Path.Combine(GetOwnDirectory(composer, entity, immediateParent), $"{composer.GetBaseName(entity)}.md");
}

/// <summary>
/// Composes a single-file entity: a <c>{container}/{baseName}.md</c> layout where the entity's markdown file sits
/// directly inside its resolved container.
/// </summary>
internal sealed class SingleFileStorageStrategy : IVaultStorageStrategy
{
	public VaultStorageShape Shape => VaultStorageShape.SingleFile;

	public string GetOwnDirectory(VaultStoragePathComposer composer, object entity, object? immediateParent)
		=> composer.GetContainer(entity, immediateParent);

	public string GetFilePath(VaultStoragePathComposer composer, object entity, object? immediateParent)
		=> Path.Combine(GetOwnDirectory(composer, entity, immediateParent), $"{composer.GetBaseName(entity)}.md");
}

/// <summary>
/// Composes a lore page: RelativePath-authoritative when a vault-relative path is known, otherwise a self-named
/// folder under the saga root named from the terminal PUCK segment. Lore placement never composes beneath a parent
/// lore page here — nesting is carried by <see cref="LorePage.RelativePath"/> — so the immediate parent is ignored.
/// </summary>
internal sealed class LorePageStorageStrategy : IVaultStorageStrategy
{
	public VaultStorageShape Shape => VaultStorageShape.SelfNamedDirectory;

	public string GetFilePath(VaultStoragePathComposer composer, object entity, object? immediateParent)
	{
		var lorePage = (LorePage)entity;

		if (!string.IsNullOrWhiteSpace(lorePage.RelativePath))
		{
			var path = Path.GetFullPath(Path.Combine(composer.Layout.VaultRoot, lorePage.RelativePath));
			var normalizedRoot = composer.Layout.VaultRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			if (path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
			{
				return path;
			}
		}

		var folderName = PuckNamedIdentity.FormatFileName(lorePage.EffectiveIdentifier, lorePage.Title);
		return Path.Combine(composer.Layout.SagaRoot, folderName, $"{folderName}.md");
	}

	public string GetOwnDirectory(VaultStoragePathComposer composer, object entity, object? immediateParent)
		=> Path.GetDirectoryName(GetFilePath(composer, entity, immediateParent))
			?? throw new InvalidOperationException("Lore page markdown path does not have a directory.");
}
