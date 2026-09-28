using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Saga;
using System.Reflection;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Database;
using Pleiades.Vault.Policy;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Centralizes path-based sync model detection for vault-backed entity types.
/// </summary>
public sealed class VaultPathSyncModelCatalog(VaultLayout layout, VaultWatcherPathPolicy pathPolicy)
{
	private static readonly string? ObjectivePartitionName = ResolvePartitionUnder(typeof(Objective));
	private static readonly string? FatePartitionName = ResolvePartitionUnder(typeof(Fate));
	private static readonly string? DecreePartitionName = ResolvePartitionUnder(typeof(Decree));
	// Whether each incentive kind is identity-driven (freeform/implicit) drives its detection scope: an identity-driven
	// child is detected anywhere inside its hosting parent, while a location-fixed (path-bound) child must sit in the
	// partition folder. Read from the declared storage mode rather than hard-coded, so it stays policy-derived.
	private static readonly bool ObjectiveIdentityDriven = ResolveModeIdentityDriven(typeof(Objective));
	private static readonly bool FateIdentityDriven = ResolveModeIdentityDriven(typeof(Fate));
	private static readonly bool DecreeIdentityDriven = ResolveModeIdentityDriven(typeof(Decree));

	private readonly object _modelsGate = new();
	private string? _modelsVaultRoot;
	private IReadOnlyList<VaultPathSyncModel>? _models;

	/// <summary>
	/// Gets the path-resolvable sync models for the currently bound vault, rebuilding them when the vault changes.
	/// </summary>
	/// <remarks>
	/// The catalog is a process-wide singleton, but the served vault can change at runtime as the core is
	/// activated and deactivated. Models are therefore built lazily against the active vault root instead of in
	/// the constructor, so the singleton can be created while the core is idle and no vault is bound.
	/// </remarks>
	private IReadOnlyList<VaultPathSyncModel> Models
	{
		get
		{
			var vaultRoot = layout.VaultRoot;
			lock (_modelsGate)
			{
				if (_models is null || !string.Equals(_modelsVaultRoot, vaultRoot, StringComparison.OrdinalIgnoreCase))
				{
					_models = BuildModels();
					_modelsVaultRoot = vaultRoot;
				}

				return _models;
			}
		}
	}

	private IReadOnlyList<VaultPathSyncModel> BuildModels() =>
	[
		// The directive family is polymorphic: the abstract Directive anchors identity/known-id lookups (spanning
		// the whole discriminated family), while path composition materializes the concrete member the file's own
		// identity selects — A… → stellar, LUNA… → lunar — via VaultFamilyInstantiationResolver. The declared
		// concreteType is only the fallback for a brand-new file with no resolvable identity yet (keeping today's
		// stellar default), never a hard-coded collapse of the family to one member.
		CreateModel<Directive>(layout, [layout.VaultRoot], VaultStorageShape.SelfNamedDirectory, static _ => false,
			concreteType: typeof(StellarDirective)),

		CreateModel<Objective>(layout, [layout.ObjectivesRoot, layout.VaultRoot], VaultStorageShape.SingleFile, path =>
			IsIncentiveMarkdownFile(typeof(Objective), path, pathPolicy, layout.ObjectivesRoot, ObjectivePartitionName, ObjectiveIdentityDriven)),

		CreateModel<Fate>(layout, [layout.FatesRoot, layout.VaultRoot], VaultStorageShape.SingleFile, path =>
			IsIncentiveMarkdownFile(typeof(Fate), path, pathPolicy, layout.FatesRoot, FatePartitionName, FateIdentityDriven)),

		CreateModel<Decree>(layout, [layout.DecreesRoot, layout.VaultRoot], VaultStorageShape.SingleFile, path =>
			IsIncentiveMarkdownFile(typeof(Decree), path, pathPolicy, layout.DecreesRoot, DecreePartitionName, DecreeIdentityDriven)),

		CreateModel<OnrushSprint>(layout, [layout.OnrushRoot], VaultStorageShape.SelfNamedDirectory, path =>
			IsPrimarySelfNamedEntityFile(path, layout.OnrushRoot)),

		CreateModel<ExecutiveOrder>(layout, [layout.OnrushRoot], VaultStorageShape.SingleFile, path =>
			IsContainedChildMarkdownFile(typeof(ExecutiveOrder), path, pathPolicy)),

		CreateModel<PolarisCycle>(layout, [layout.JournalRoot], VaultStorageShape.SingleFile, static path =>
			string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase)),

		CreateModel<LorePage>(layout, [layout.SagaRoot], VaultStorageShape.SelfNamedDirectory, path =>
			IsPrimarySelfNamedEntityFile(path, layout.SagaRoot)),
	];

	/// <summary>
	/// Gets all known path-resolvable sync models.
	/// </summary>
	public IReadOnlyList<VaultPathSyncModel> GetModels() => Models;

	/// <summary>
	/// Validates that the declared path-sync models are coherent with, and fully cover, the entity model catalog:
	/// every model targets a vault-stored catalog entity of the declared shape, and every concrete vault-stored
	/// entity is discoverable through a model — directly, or via a family anchor spanning its members. This keeps the
	/// path-sync list a faithful projection of the catalog rather than a parallel source of truth, failing vault
	/// activation fast on drift such as a new vault-stored entity added without a model (which would otherwise be
	/// silently undiscoverable) or a shape that disagrees with its declaration.
	/// </summary>
	/// <param name="entityModelCatalog">The declarative entity model catalog the path-sync list projects from.</param>
	public void ValidateAgainstCatalog(VaultEntityModelCatalog entityModelCatalog)
	{
		ArgumentNullException.ThrowIfNull(entityModelCatalog);

		var models = Models;
		foreach (var model in models)
		{
			var declared = entityModelCatalog.GetRequired(model.EntityType);
			if (declared.Storage is null)
			{
				throw new InvalidOperationException(
					$"Path-sync model '{model.EntityName}' targets an entity that declares no vault storage.");
			}

			if (declared.Storage.Shape != model.Shape)
			{
				throw new InvalidOperationException(
					$"Path-sync model '{model.EntityName}' declares shape '{model.Shape}', but entity storage declares '{declared.Storage.Shape}'.");
			}
		}

		foreach (var declared in entityModelCatalog.GetModels())
		{
			if (declared.IsAbstract || declared.Storage is null)
			{
				continue;
			}

			var covered = models.Any(model =>
				model.EntityType == declared.EntityType
				|| model.EntityType.IsAssignableFrom(declared.EntityType));
			if (!covered)
			{
				throw new InvalidOperationException(
					$"Vault-stored entity '{declared.EntityType.Name}' has no path-sync model and would be undiscoverable; declare a model, or a family anchor that spans it.");
			}
		}
	}

	/// <summary>
	/// Gets all distinct scan roots ordered by specificity.
	/// </summary>
	public IReadOnlyList<string> GetScanRoots()
	{
		return Models
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
		return Models
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
		model = Models
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
		model = Models
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
			static (context, cancellationToken) => VaultEntityGateway.LoadKnownIdsAsync(context, typeof(T), cancellationToken),
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
	/// <param name="entityType">The incentive kind being classified, whose declared containment applies.</param>
	/// <param name="path">The markdown file path to classify.</param>
	/// <param name="pathPolicy">The single containment resolver.</param>
	/// <param name="standaloneRoot">The kind's standalone root directory.</param>
	/// <param name="partitionName">The kind's directive partition folder name.</param>
	/// <returns><see langword="true"/> when the file is a candidate of this incentive kind.</returns>
	private static bool IsIncentiveMarkdownFile(Type entityType, string path, VaultWatcherPathPolicy pathPolicy, string standaloneRoot, string? partitionName, bool isIdentityDriven)
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

		// The kind's own standalone root: an incentive authored directly under it, with no hosting directive.
		if (string.Equals(parentDirectory, standaloneRoot, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		// Otherwise the note must be contained by a parent of the kind's declared type. Resolution goes through the one
		// shared containment resolver so detection cannot drift from the watcher/composer.
		if (string.IsNullOrWhiteSpace(pathPolicy.TryResolveContainingParentId(entityType, path)))
		{
			return false;
		}

		// Kind is disambiguated by the partition folder that encloses the note (at any depth): a note inside a sibling
		// kind's partition — Fates/… for the objective model — belongs to that sibling, not here.
		var enclosingPartition = TryFindEnclosingEntityPartition(path, pathPolicy);
		if (enclosingPartition is not null)
		{
			return string.Equals(enclosingPartition, partitionName, StringComparison.OrdinalIgnoreCase);
		}

		// Un-partitioned under the directive (directly in it, or in a plain subfolder): an identity-driven child
		// (freeform/implicit) is detected anywhere inside its parent — it only becomes an entity through
		// initialisation — while a location-fixed (path-bound) child must sit in its partition folder.
		return isIdentityDriven;
	}

	/// <summary>
	/// Finds the entity partition folder enclosing a markdown path — a folder the path policy recognises as a partition
	/// (<see cref="VaultWatcherPathPolicy.IsPartitionDirectory"/>) — walking up until the hosting entity's own folder
	/// (<see cref="VaultWatcherPathPolicy.IsEntityFolder"/>, recognised by what its main note asserts, whatever the names).
	/// Returns <see langword="null"/> when the note is not inside any partition: it sits directly in the hosting entity's
	/// folder or a plain subfolder of it, including an entity's folder that merely bears a partition's name.
	/// </summary>
	private static string? TryFindEnclosingEntityPartition(string path, VaultWatcherPathPolicy pathPolicy)
	{
		var currentDirectory = Path.GetDirectoryName(path);
		while (!string.IsNullOrWhiteSpace(currentDirectory) && !pathPolicy.ShouldIgnorePath(currentDirectory))
		{
			if (pathPolicy.IsPartitionDirectory(currentDirectory))
			{
				return Path.GetFileName(currentDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			}

			// Reaching the hosting entity's own folder without having crossed a partition means the note is un-partitioned.
			if (pathPolicy.IsEntityFolder(currentDirectory))
			{
				return null;
			}

			currentDirectory = Directory.GetParent(currentDirectory)?.FullName;
		}

		return null;
	}

	/// <summary>
	/// Determines whether a markdown file path is a candidate of an entity kind that only exists inside its parent: a note
	/// (not a self-named primary) whose location a parent of the kind's declared type contains, per its declared
	/// containment — for a path-bound kind, exactly its composed place (the parent's folder, or its declared partition).
	/// </summary>
	/// <param name="entityType">The contained kind being classified.</param>
	/// <param name="path">The markdown file path to classify.</param>
	/// <param name="pathPolicy">The single containment resolver.</param>
	/// <returns><see langword="true"/> when the file is a candidate of the kind.</returns>
	private static bool IsContainedChildMarkdownFile(Type entityType, string path, VaultWatcherPathPolicy pathPolicy)
	{
		if (!string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		if (MarkdownFileLocator.IsPrimarySelfNamedFile(path))
		{
			return false;
		}

		return !string.IsNullOrWhiteSpace(pathPolicy.TryResolveContainingParentId(entityType, path));
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

	/// <summary>
	/// Reads whether an entity's declared storage mode is identity-driven, which decides its child-detection scope
	/// (identity-driven children are detected anywhere inside their parent; location-fixed children only in the
	/// partition). Derived from the declared mode so the rule stays policy-driven rather than per-kind hard-coding.
	/// </summary>
	private static bool ResolveModeIdentityDriven(Type entityType)
	{
		var mode = entityType.GetCustomAttribute<VaultStorageAttribute>()?.Mode
			?? throw new InvalidOperationException($"Type '{entityType.Name}' must declare {nameof(VaultStorageAttribute)} to classify its detection scope.");
		return mode.IsIdentityDriven();
	}
}