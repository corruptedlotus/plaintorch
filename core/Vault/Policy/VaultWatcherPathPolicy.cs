using System.Text.Json;
using Pleiades.Puck;
using Pleiades.Resources;
using Pleiades.Vault.Markdown;

using Pleiades.Vault.Watcher;

namespace Pleiades.Vault.Policy;

/// <summary>
/// Centralizes watcher path filtering and scan-root selection rules defined by the watcher blueprint, and is the single
/// resolver of <em>containment</em>: which parent entity a note's location implies.
/// </summary>
/// <remarks>
/// Containment is storage policy, read from declarations alone (<see cref="VaultEntityModelCatalog"/>): an entity's
/// <see cref="VaultStorageAttribute.ParentEntityType"/> says what kind of entity may contain its note, the parent's own
/// declaration says what a folder of that kind is, and the entity's declaration says how far from that folder its note
/// may sit. No caller asks about a particular entity type; see <see cref="EnumerateContainingParentIds"/>.
/// </remarks>
public sealed class VaultWatcherPathPolicy(VaultLayout layout, VaultEntityModelCatalog entityModelCatalog, PuckTokenizer puckTokenizer)
{
	private const string ObsidianConfigRelativePath = ".obsidian/app.json";
	private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

	/// <summary>
	/// Returns distinct watcher roots, optionally including full-vault observation when any model is freeform.
	/// </summary>
	public IReadOnlyList<string> GetWatchRoots(VaultPathSyncModelCatalog modelCatalog)
	{
		ArgumentNullException.ThrowIfNull(modelCatalog);

		var roots = modelCatalog
			.GetScanRoots()
			.Select(Path.GetFullPath)
			.ToHashSet(PathComparer);

		if (modelCatalog.GetModels().Any(static model => model.Mode.IsIdentityDriven()))
		{
			roots.Add(Path.GetFullPath(layout.VaultRoot));
		}

		return roots
			.OrderByDescending(static root => root.Length)
			.ToArray();
	}

	/// <summary>
	/// Determines whether the vault is structurally reachable: the vault root and every entity root that exists can be
	/// enumerated. A failure here is a whole-of-vault (tier-2) condition — an unmounted drive, a revoked permission on a
	/// root — that the watcher answers by going to sleep and periodically re-probing, rather than by retrying a single
	/// file. A root that simply does not exist yet (never created) is not a failure; only one that exists but cannot be
	/// accessed is. When it returns <see langword="false"/>, <paramref name="inaccessiblePath"/> names the offending root.
	/// </summary>
	public bool IsVaultStructurallyAccessible(out string? inaccessiblePath)
	{
		inaccessiblePath = null;
		var vaultRoot = Path.GetFullPath(layout.VaultRoot);
		if (!Directory.Exists(vaultRoot) || !CanEnumerate(vaultRoot))
		{
			inaccessiblePath = vaultRoot;
			return false;
		}

		foreach (var root in EntityRoots())
		{
			var fullRoot = Path.GetFullPath(root);
			if (Directory.Exists(fullRoot) && !CanEnumerate(fullRoot))
			{
				inaccessiblePath = fullRoot;
				return false;
			}
		}

		return true;
	}

	private static bool CanEnumerate(string directory)
	{
		try
		{
			// Directory.Exists hides permission/IO failures behind a bare false; force an actual read of the first entry
			// so a revoked-access or unmounted root surfaces as the structural failure it is.
			using var enumerator = Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
			enumerator.MoveNext();
			return true;
		}
		catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
		{
			return false;
		}
	}

	/// <summary>
	/// The declared location root of every vault-stored entity, and the metadata root: the folders the vault layout
	/// grants to PLAINTORCH, derived from the catalog rather than listed per entity.
	/// </summary>
	private IEnumerable<string> EntityRoots()
	{
		return entityModelCatalog.GetModels()
			.Where(static model => model.Storage is not null)
			.Select(model => layout.GetLocationRoot(model.Storage!.LocationKey))
			.Append(layout.MetadataRoot)
			.Distinct(PathComparer);
	}

	/// <summary>
	/// Determines whether the watcher should ignore a path for scan and live-event processing.
	/// </summary>
	public bool ShouldIgnorePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return true;
		}

		var fullPath = Path.GetFullPath(path);
		if (!IsUnderVaultRoot(fullPath))
		{
			return true;
		}

		if (IsUnderAttachmentFolder(fullPath))
		{
			return true;
		}

		var relativePath = Path.GetRelativePath(layout.VaultRoot, fullPath);
		if (string.IsNullOrWhiteSpace(relativePath)
			|| string.Equals(relativePath, ".", StringComparison.Ordinal))
		{
			return false;
		}

		return relativePath
			.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
			.Any(static segment => segment.StartsWith("_", StringComparison.Ordinal) || segment.StartsWith(".", StringComparison.Ordinal));
	}

	/// <summary>
	/// Gets why a note may not assert the identity of an entity of the given kind where it sits, or <see langword="null"/>
	/// when it may. Identity-driven kinds (Freeform, Implicit) may be asserted anywhere in the vault; these are the limits
	/// of "anywhere", read from the kind's declarations rather than from any particular kind:
	/// <list type="bullet">
	/// <item>A self-named kind's note owns the folder it sits in, so that folder may not be the vault root, a declared
	/// entity root, or a partition.</item>
	/// <item>A note may not sit under a root that another kind is declared in — any kind outside its own family and the
	/// families of the kinds that may contain it (<see cref="VaultStorageAttribute.ParentEntityType"/>, transitively) — nor
	/// under the metadata root.</item>
	/// </list>
	/// </summary>
	/// <param name="entityType">The kind whose identity the note asserts.</param>
	/// <param name="path">The note's path.</param>
	public string? TryGetAssertionViolation(Type entityType, string path)
	{
		ArgumentNullException.ThrowIfNull(entityType);
		if (string.IsNullOrWhiteSpace(path))
		{
			return WatcherMessages.PathViolations.PathEmpty;
		}

		var fullPath = Path.GetFullPath(path);
		if (!IsUnderVaultRoot(fullPath))
		{
			return WatcherMessages.PathViolations.PathOutsideVaultRoot;
		}

		if (!string.Equals(Path.GetExtension(fullPath), ".md", StringComparison.OrdinalIgnoreCase))
		{
			return WatcherMessages.PathViolations.PathNotMarkdown;
		}

		var directory = Path.GetDirectoryName(fullPath);
		if (string.IsNullOrWhiteSpace(directory))
		{
			return WatcherMessages.PathViolations.PathHasNoDirectory;
		}

		if (!entityModelCatalog.TryGet(entityType, out var model) || model?.Storage is not { } storage)
		{
			return null;
		}

		if (storage.Shape == VaultStorageShape.SelfNamedDirectory)
		{
			if (IsVaultRootDirectory(directory))
			{
				return WatcherMessages.PathViolations.CannotOwnVaultRoot;
			}

			if (IsEntityRootDirectory(directory))
			{
				return WatcherMessages.PathViolations.CannotOwnEntityRoot;
			}

			if (IsPartitionDirectory(directory))
			{
				return WatcherMessages.PathViolations.CannotOwnPartition;
			}
		}

		return IsUnderForeignRoot(directory, entityType)
			? WatcherMessages.PathViolations.UnderForeignRoot
			: null;
	}

	/// <summary>
	/// Whether a path lies under the metadata root or a root declared by a kind foreign to <paramref name="entityType"/>:
	/// outside its own family and the families of the kinds that may contain it, transitively. A root a related kind also
	/// declares (several kinds sharing a location) is never foreign.
	/// </summary>
	private bool IsUnderForeignRoot(string path, Type entityType)
	{
		var fullPath = Path.GetFullPath(path);
		if (IsUnderRoot(fullPath, layout.MetadataRoot))
		{
			return true;
		}

		var related = RelatedKinds(entityType);
		var relatedRoots = DeclaredRoots(model => related.Contains(model.EntityType)).ToArray();
		return DeclaredRoots(model => !related.Contains(model.EntityType))
			.Where(root => !relatedRoots.Contains(root, PathComparer))
			.Any(root => IsUnderRoot(fullPath, root));
	}

	/// <summary>
	/// A kind's own family, and the families of every kind that may contain it through its declared
	/// <see cref="VaultStorageAttribute.ParentEntityType"/>, transitively: the kinds whose territory it may share.
	/// </summary>
	private IReadOnlySet<Type> RelatedKinds(Type entityType)
	{
		var related = new HashSet<Type>();
		for (Type? kind = entityType; kind is not null && !related.Contains(kind);)
		{
			related.UnionWith(Family(kind));
			kind = entityModelCatalog.TryGet(kind, out var model) ? model?.Storage?.ParentEntityType : null;
		}

		return related;
	}

	/// <summary>A kind, its family anchor, and the anchor's concrete members.</summary>
	private IEnumerable<Type> Family(Type entityType)
	{
		var anchor = entityModelCatalog.GetFamilyAnchor(entityType) ?? entityType;
		var members = entityModelCatalog.TryGetFamily(anchor, out var family) && family is not null
			? family.Members.Select(static member => member.EntityType)
			: [];
		return members.Prepend(anchor).Append(entityType).Distinct();
	}

	/// <summary>
	/// Resolves the parent a note's location implies for an entity type: the nearest candidate of
	/// <see cref="EnumerateContainingParentIds"/>, or <see langword="null"/> when no folder of the declared parent type
	/// contains the note (or the type declares no parent).
	/// </summary>
	/// <param name="entityType">The entity type whose declared containment applies to the note.</param>
	/// <param name="path">The note's path (a folder path is read as the container itself).</param>
	public string? TryResolveContainingParentId(Type entityType, string? path)
		=> EnumerateContainingParentIds(entityType, path).FirstOrDefault();

	/// <summary>
	/// Enumerates, nearest first, the identities of the folders of the entity's declared parent type that contain a note —
	/// the parents its location may imply. A caller that knows which identities exist takes the first known one, so a
	/// folder asserting an identity the vault does not hold never parents anything; a caller that does not takes the first.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Everything is read from storage declarations:
	/// </para>
	/// <list type="bullet">
	/// <item>The entity's <see cref="VaultStorageAttribute.ParentEntityType"/> names the parent kind; without one there is
	/// no containment.</item>
	/// <item>Only a <see cref="VaultStorageShape.SelfNamedDirectory"/> parent owns a folder. A folder is a parent's when a
	/// note in it asserts an identity the parent kind's declared PUCK notation (or a family member's) could mint: its
	/// self-named primary note, read per the parent's <see cref="VaultStorageAttribute.PuckStorage"/>, or — when the
	/// parent's policy is identity-driven, so its note may bear any name — any note directly in it. An identity of any
	/// other kind (a sibling child's note, the note itself) says nothing about the folder.</item>
	/// <item>A folder is only considered inside the parent's territory: never the vault root, a declared entity root or
	/// a declared partition folder; for an identity-driven parent, anywhere outside the roots other kinds are declared in;
	/// for a path-bound parent, only under the roots its own kind is declared in.</item>
	/// <item>An entity whose policy is identity-driven may sit anywhere inside its parent's folder, so every enclosing
	/// parent folder is a candidate; a path-bound entity sits exactly where its composed path puts it — in the parent's
	/// folder, or in the partition it declares there. A self-named entity's own folder is the entity itself, never its
	/// container.</item>
	/// </list>
	/// <para>
	/// Resolution reads only the folders around the note, never the note itself, so path classification can ask it about a
	/// note another process holds open. A caller that knows the entity's own identity skips it among the candidates (a
	/// second note asserting it elsewhere is a duplicate identity, never a container).
	/// </para>
	/// </remarks>
	/// <param name="entityType">The entity type whose declared containment applies to the note.</param>
	/// <param name="path">The note's path (a folder path is read as the container itself).</param>
	public IEnumerable<string> EnumerateContainingParentIds(Type entityType, string? path)
	{
		ArgumentNullException.ThrowIfNull(entityType);
		if (string.IsNullOrWhiteSpace(path)
			|| !TryGetContainment(entityType, out var storage, out var parent))
		{
			yield break;
		}

		var fullPath = Path.GetFullPath(path);
		var isFolder = Directory.Exists(fullPath);
		var directory = isFolder ? fullPath : Path.GetDirectoryName(fullPath);
		if (!isFolder
			&& storage.Shape == VaultStorageShape.SelfNamedDirectory
			&& MarkdownFileLocator.IsPrimarySelfNamedFile(fullPath))
		{
			directory = Directory.GetParent(directory!)?.FullName;
		}

		if (string.IsNullOrWhiteSpace(directory))
		{
			yield break;
		}

		if (!storage.Mode.IsIdentityDriven())
		{
			var composedHost = ResolveComposedHost(directory, storage);
			if (composedHost is not null && TryReadParentIdentity(composedHost, fullPath, parent) is { } composedId)
			{
				yield return composedId;
			}

			yield break;
		}

		for (var current = directory; !string.IsNullOrWhiteSpace(current) && IsUnderVaultRoot(current); current = Directory.GetParent(current)?.FullName)
		{
			if (TryReadParentIdentity(current, fullPath, parent) is { } id)
			{
				yield return id;
			}
		}
	}

	/// <summary>
	/// The declared parent kind of a containment, with everything needed to recognise its folders: its storage, the
	/// notations its family mints, and the roots its family is declared in.
	/// </summary>
	private sealed record ParentKind(
		Type Type,
		VaultStorageAttribute Storage,
		IReadOnlyList<string> Declarations,
		IReadOnlySet<Type> Family);

	private bool TryGetContainment(Type entityType, out VaultStorageAttribute storage, out ParentKind parent)
	{
		storage = null!;
		parent = null!;
		if (!entityModelCatalog.TryGet(entityType, out var model)
			|| model?.Storage is not { ParentEntityType: { } parentType } declared
			|| !entityModelCatalog.TryGet(parentType, out var parentModel)
			|| parentModel?.Storage is not { Shape: VaultStorageShape.SelfNamedDirectory } parentStorage)
		{
			return false;
		}

		var familyTypes = Family(parentType).ToHashSet();
		var declarations = familyTypes
			.Select(type => entityModelCatalog.TryGet(type, out var member) ? member?.PuckDeclaration : null)
			.Where(static declaration => !string.IsNullOrWhiteSpace(declaration))
			.Select(static declaration => declaration!)
			.Distinct(StringComparer.Ordinal)
			.ToArray();

		storage = declared;
		parent = new ParentKind(parentType, parentStorage, declarations, familyTypes);
		return true;
	}

	/// <summary>
	/// The one folder a path-bound entity's composed path puts its parent at: the folder holding the note, or — when the
	/// entity declares a partition — the folder holding that partition, provided the note sits directly in it. Where the
	/// partition cannot be used (<see cref="IsPartitionUsable"/>), the composed place is the parent's folder itself, so a
	/// note sitting directly in it is in place.
	/// </summary>
	private string? ResolveComposedHost(string directory, VaultStorageAttribute storage)
	{
		var partition = storage.PartitionUnder?.Trim();
		if (string.IsNullOrWhiteSpace(partition))
		{
			return directory;
		}

		if (string.Equals(FolderName(directory), partition, StringComparison.OrdinalIgnoreCase) && IsPartitionDirectory(directory))
		{
			return Directory.GetParent(directory)?.FullName;
		}

		return IsPartitionUsable(directory, partition) ? null : directory;
	}

	/// <summary>
	/// Whether a folder is a partition: named as some kind declares its partition (<see cref="VaultStorageAttribute.PartitionUnder"/>),
	/// and not a self-named folder — an entity's own folder (a directive titled like a partition) is never a partition,
	/// whatever its name. A partition belongs to the folder holding it, so it never hosts a parent itself.
	/// </summary>
	/// <param name="directory">The folder to classify.</param>
	public bool IsPartitionDirectory(string directory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directory);
		var name = FolderName(directory);
		return !string.IsNullOrWhiteSpace(name)
			&& PartitionNames().Contains(name)
			&& !MarkdownFileLocator.IsSelfNamedDirectory(directory);
	}

	/// <summary>
	/// Whether a declared partition can be used beneath a parent's folder. It cannot when the parent's folder is itself named
	/// like the partition (a partition inside it would repeat its name), when a folder of that name exists but is an
	/// entity's own folder (self-named), or — while no such folder exists yet — when a file of that name (with or without
	/// an extension) sits in the parent's folder. An existing plain folder of that name is the partition. Where a partition
	/// cannot be used, children are placed directly in the parent's folder instead.
	/// </summary>
	/// <param name="parentDirectory">The folder of the parent the child belongs to.</param>
	/// <param name="partition">The partition name the child's kind declares.</param>
	public bool IsPartitionUsable(string parentDirectory, string partition)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
		ArgumentException.ThrowIfNullOrWhiteSpace(partition);
		if (string.Equals(FolderName(parentDirectory), partition, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var partitionDirectory = Path.Combine(parentDirectory, partition);
		if (Directory.Exists(partitionDirectory))
		{
			return !MarkdownFileLocator.IsSelfNamedDirectory(partitionDirectory);
		}

		return !Directory.Exists(parentDirectory)
			|| !Directory.EnumerateFiles(parentDirectory, "*", SearchOption.TopDirectoryOnly).Any(file =>
				PathComparer.Equals(Path.GetFileName(file), partition)
				|| PathComparer.Equals(Path.GetFileNameWithoutExtension(file), partition));
	}

	private IReadOnlySet<string> PartitionNames()
	{
		return entityModelCatalog.GetModels()
			.Select(static model => model.Storage?.PartitionUnder?.Trim())
			.Where(static partition => !string.IsNullOrWhiteSpace(partition))
			.Select(static partition => partition!)
			.ToHashSet(PathComparer);
	}

	private static string FolderName(string directory)
		=> Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

	/// <summary>
	/// Reads the identity of the parent a folder is, or <see langword="null"/> when it is none: outside the parent kind's
	/// territory, or holding no note (other than <paramref name="resolvedNote"/> itself) that asserts an identity of that
	/// kind.
	/// </summary>
	private string? TryReadParentIdentity(string directory, string resolvedNote, ParentKind parent)
	{
		if (!IsParentTerritory(directory, parent) || !Directory.Exists(directory))
		{
			return null;
		}

		var primary = Path.Combine(directory, $"{Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}.md");
		if (!PathComparer.Equals(primary, resolvedNote)
			&& File.Exists(primary)
			&& ReadAssertedIdentity(primary, parent.Storage) is { } primaryId
			&& IsParentIdentity(primaryId, parent))
		{
			return primaryId;
		}

		if (!parent.Storage.Mode.IsIdentityDriven())
		{
			return null;
		}

		foreach (var note in Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly).Order(PathComparer))
		{
			if (PathComparer.Equals(note, resolvedNote) || PathComparer.Equals(note, primary))
			{
				continue;
			}

			if (MarkdownFileLocator.TryReadFrontMatterPuck(note) is { } id && IsParentIdentity(id, parent))
			{
				return id;
			}
		}

		return null;
	}

	/// <summary>
	/// Reads the identity a note asserts under a storage form: a quiet note's frontmatter PUCK, or an indexed note's
	/// filename token (the name a self-named folder shares with its primary note).
	/// </summary>
	private static string? ReadAssertedIdentity(string notePath, VaultStorageAttribute storage)
	{
		return storage.PuckStorage == VaultPuckStorage.Index
			? PuckNamedIdentity.ParseLoose(PuckNamedIdentity.DecodeFileName(notePath)).Id
			: MarkdownFileLocator.TryReadFrontMatterPuck(notePath);
	}

	/// <summary>
	/// Whether an identity is one the parent kind could mint — it tokenizes against the declared notation of the kind or
	/// a member of its family — so an identity of another kind is never read as the parent's.
	/// </summary>
	private bool IsParentIdentity(string identity, ParentKind parent)
	{
		foreach (var declaration in parent.Declarations)
		{
			try
			{
				puckTokenizer.Tokenize(declaration, identity);
				return true;
			}
			catch (Exception exception) when (exception is FormatException or InvalidOperationException or NotSupportedException)
			{
			}
		}

		return false;
	}

	/// <summary>
	/// Whether a folder lies in the territory a parent kind's folders may occupy: inside the vault, never the vault root,
	/// a declared entity root or a partition; for an identity-driven kind, outside every root foreign to it (the same
	/// territory its notes may be asserted in, <see cref="TryGetAssertionViolation"/>); for a path-bound kind, under a root
	/// its own family is declared in.
	/// </summary>
	private bool IsParentTerritory(string directory, ParentKind parent)
	{
		var fullPath = Path.GetFullPath(directory);
		if (!IsUnderVaultRoot(fullPath)
			|| IsVaultRootDirectory(fullPath)
			|| IsEntityRootDirectory(fullPath)
			|| IsPartitionDirectory(fullPath))
		{
			return false;
		}

		if (!parent.Storage.Mode.IsIdentityDriven())
		{
			return DeclaredRoots(model => parent.Family.Contains(model.EntityType)).Any(root => IsUnderRoot(fullPath, root));
		}

		return !IsUnderForeignRoot(fullPath, parent.Type);
	}

	private IEnumerable<string> DeclaredRoots(Func<VaultEntityModel, bool> predicate)
	{
		return entityModelCatalog.GetModels()
			.Where(model => model.Storage is not null && predicate(model))
			.Select(model => Path.GetFullPath(layout.GetLocationRoot(model.Storage!.LocationKey)))
			.Distinct(PathComparer);
	}

	private bool IsUnderVaultRoot(string fullPath)
	{
		var normalizedRoot = Path.GetFullPath(layout.VaultRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), normalizedRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
	}

	private bool IsUnderAttachmentFolder(string fullPath)
	{
		var attachmentFolderPath = TryResolveAttachmentFolderPath();
		if (string.IsNullOrWhiteSpace(attachmentFolderPath))
		{
			return false;
		}

		var normalizedFolder = attachmentFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		return fullPath.StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), normalizedFolder.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
	}

	private bool IsVaultRootDirectory(string fullPath)
	{
		var normalizedPath = Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var normalizedVaultRoot = Path.GetFullPath(layout.VaultRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return string.Equals(normalizedPath, normalizedVaultRoot, StringComparison.OrdinalIgnoreCase);
	}

	private bool IsEntityRootDirectory(string fullPath)
	{
		return EntityRoots().Any(root => IsDirectoryEqual(fullPath, root));
	}

	private static bool IsDirectoryEqual(string path, string other)
	{
		var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var normalizedOther = Path.GetFullPath(other).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return string.Equals(normalizedPath, normalizedOther, StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsUnderRoot(string fullPath, string rootPath)
	{
		var normalizedRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), normalizedRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
	}

	private string? TryResolveAttachmentFolderPath()
	{
		var obsidianConfigPath = Path.Combine(layout.VaultRoot, ObsidianConfigRelativePath.Replace('/', Path.DirectorySeparatorChar));
		if (!File.Exists(obsidianConfigPath))
		{
			return null;
		}

		try
		{
			using var stream = File.OpenRead(obsidianConfigPath);
			using var document = JsonDocument.Parse(stream);
			if (!document.RootElement.TryGetProperty("attachmentFolderPath", out var property)
				|| property.ValueKind != JsonValueKind.String)
			{
				return null;
			}

			var configuredPath = property.GetString();
			if (string.IsNullOrWhiteSpace(configuredPath)
				|| string.Equals(configuredPath, "./", StringComparison.Ordinal)
				|| string.Equals(configuredPath, "/", StringComparison.Ordinal)
				|| string.Equals(configuredPath, "current", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}

			var normalizedRelative = configuredPath
				.Replace('/', Path.DirectorySeparatorChar)
				.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
				.Trim();
			if (string.IsNullOrWhiteSpace(normalizedRelative))
			{
				return null;
			}

			return Path.GetFullPath(Path.Combine(layout.VaultRoot, normalizedRelative));
		}
		catch
		{
			return null;
		}
	}
}
