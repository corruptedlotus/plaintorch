using System.Text.Json;
using System.Reflection;
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
	private static readonly HashSet<string> PartitionFolderNames = ResolvePartitionFolderNames();

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
	/// Determines whether a freeform directive may assert its path as authoritative.
	/// </summary>
	public bool IsAllowedFreeformDirectiveAssertionPath(string path)
	{
		return TryGetFreeformDirectiveAssertionViolation(path) is null;
	}

	/// <summary>
	/// Gets a human-readable violation reason when a freeform directive assertion path is not allowed.
	/// </summary>
	public string? TryGetFreeformDirectiveAssertionViolation(string path)
	{
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

		if (IsUnderNonDirectiveManagedRoot(directory))
		{
			return WatcherMessages.PathViolations.UnderNonDirectiveRoot;
		}

		var parent = Directory.GetParent(directory)?.FullName;
		if (!string.IsNullOrWhiteSpace(parent) && IsUnderNonDirectiveManagedRoot(parent))
		{
			return WatcherMessages.PathViolations.ParentUnderNonDirectiveRoot;
		}

		return null;
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

		var anchor = entityModelCatalog.GetFamilyAnchor(parentType) ?? parentType;
		var members = entityModelCatalog.TryGetFamily(anchor, out var family) && family is not null
			? family.Members.Select(static member => member.EntityType)
			: [];
		var familyTypes = members.Prepend(anchor).Append(parentType).ToHashSet();
		var declarations = familyTypes
			.Select(type => entityModelCatalog.TryGet(type, out var member) ? member?.PuckDeclaration : null)
			.Where(static declaration => !string.IsNullOrWhiteSpace(declaration))
			.Select(static declaration => declaration!)
			.Distinct(StringComparer.Ordinal)
			.ToArray();

		storage = declared;
		parent = new ParentKind(parentStorage, declarations, familyTypes);
		return true;
	}

	/// <summary>
	/// The one folder a path-bound entity's composed path puts its parent at: the folder holding the note, or — when the
	/// entity declares a partition — the folder holding that partition, provided the note sits directly in it.
	/// </summary>
	private static string? ResolveComposedHost(string directory, VaultStorageAttribute storage)
	{
		var partition = storage.PartitionUnder?.Trim();
		if (string.IsNullOrWhiteSpace(partition))
		{
			return directory;
		}

		var directoryName = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		return string.Equals(directoryName, partition, StringComparison.OrdinalIgnoreCase)
			? Directory.GetParent(directory)?.FullName
			: null;
	}

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
	/// a declared entity root or a declared partition folder; for an identity-driven kind, outside every root another kind
	/// is declared in; for a path-bound kind, under a root its own family is declared in.
	/// </summary>
	private bool IsParentTerritory(string directory, ParentKind parent)
	{
		var fullPath = Path.GetFullPath(directory);
		if (!IsUnderVaultRoot(fullPath)
			|| IsVaultRootDirectory(fullPath)
			|| IsEntityRootDirectory(fullPath)
			|| IsPartitionDirectory(fullPath)
			|| IsUnderRoot(fullPath, layout.MetadataRoot))
		{
			return false;
		}

		var ownRoots = DeclaredRoots(model => parent.Family.Contains(model.EntityType)).ToArray();
		if (!parent.Storage.Mode.IsIdentityDriven())
		{
			return ownRoots.Any(root => IsUnderRoot(fullPath, root));
		}

		return !DeclaredRoots(model => !parent.Family.Contains(model.EntityType))
			.Where(root => !ownRoots.Contains(root, PathComparer))
			.Any(root => IsUnderRoot(fullPath, root));
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

	private bool IsUnderNonDirectiveManagedRoot(string fullPath)
	{
		return IsUnderRoot(fullPath, layout.MetadataRoot)
			|| IsUnderRoot(fullPath, layout.ObjectivesRoot)
			|| IsUnderRoot(fullPath, layout.FatesRoot)
			|| IsUnderRoot(fullPath, layout.DecreesRoot)
			|| IsUnderRoot(fullPath, layout.OnrushRoot)
			|| IsUnderRoot(fullPath, layout.JournalRoot)
			|| IsUnderRoot(fullPath, layout.SagaRoot);
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

	private static bool IsPartitionDirectory(string fullPath)
	{
		var name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		return !string.IsNullOrWhiteSpace(name) && PartitionFolderNames.Contains(name);
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

	private static HashSet<string> ResolvePartitionFolderNames()
	{
		return typeof(VaultWatcherPathPolicy).Assembly
			.GetTypes()
			.Select(type => type.GetCustomAttribute<VaultStorageAttribute>())
			.Where(attribute => attribute is not null && !string.IsNullOrWhiteSpace(attribute.PartitionUnder))
			.Select(attribute => attribute!.PartitionUnder!.Trim())
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
	}
}
