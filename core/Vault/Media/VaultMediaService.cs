using System.Collections.Concurrent;
using System.Reflection;
using Pleiades.Vault.Database;
using Pleiades.Vault.Watcher;

namespace Pleiades.Vault.Media;

/// <summary>
/// Stores, resolves, and removes binary media assets (icons, banners, and any future attachment) in the
/// vault (PEP105).
/// </summary>
/// <remarks>
/// A media key resolves two ways: <c>media:</c> for self/level media in the owning entity's own <c>_assets</c>
/// folder, and <c>vault:</c> for shared media in the vault root's <c>_assets</c> folder. A key with neither
/// scheme is a plain glyph name. The leading underscore is load-bearing:
/// <see cref="Pleiades.Vault.Policy.VaultWatcherPathPolicy"/> already skips any path segment beginning with it,
/// so binaries never reach markdown discovery.
///
/// Resolution is model-agnostic: <see cref="EnrichMedia"/> reflects over an entity's <see cref="MediaAttribute"/>
/// keys and fills their <see cref="MediaReference"/> companions, so any entity attaches media without new code
/// here. Only the self-folder (where an entity's own assets live) is model-specific and supplied by the caller.
/// </remarks>
public sealed class VaultMediaService(
	VaultLayout layout,
	VaultWatcherWriteBarrier writeBarrier,
	VaultTemporalDataService temporalDataService)
{
	/// <summary>The asset folder name; the leading underscore excludes it from markdown discovery.</summary>
	public const string AssetFolderName = "_assets";

	/// <summary>Marks a self/level media key — a file in the owning entity's own asset folder.</summary>
	public const string SelfReferencePrefix = "media:";

	/// <summary>Marks a vault-level media key — a file in the vault root's shared asset folder.</summary>
	public const string VaultReferencePrefix = "vault:";

	/// <summary>Largest asset accepted, guarding the loopback transport that carries base64 uploads.</summary>
	public const long MaxAssetBytes = 8 * 1024 * 1024;

	private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".avif", ".bmp", ".ico",
	};

	private static readonly ConcurrentDictionary<Type, IReadOnlyList<(PropertyInfo Key, PropertyInfo Companion)>> MediaPropertyCache = new();

	/// <summary>The vault-root shared asset folder that <c>vault:</c> keys resolve against.</summary>
	public string VaultAssetFolder => Path.Combine(layout.VaultRoot, AssetFolderName);

	/// <summary>
	/// Resolves the asset folder for an entity from its markdown path and storage shape.
	/// </summary>
	/// <param name="entityMarkdownPath">The absolute path of the entity's markdown file.</param>
	/// <param name="shape">The entity's physical storage shape.</param>
	/// <returns>The absolute asset folder path (not guaranteed to exist yet).</returns>
	public string GetAssetFolder(string entityMarkdownPath, VaultStorageShape shape)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(entityMarkdownPath);
		var directory = Path.GetDirectoryName(Path.GetFullPath(entityMarkdownPath))
			?? throw new InvalidOperationException($"Markdown path '{entityMarkdownPath}' has no containing directory.");

		// A folder-backed entity keeps its assets inside its own directory; a monofile entity has no directory
		// of its own, so its assets are partitioned under the shared root by the file's base name.
		return shape == VaultStorageShape.SelfNamedDirectory
			? Path.Combine(directory, AssetFolderName)
			: Path.Combine(directory, AssetFolderName, Path.GetFileNameWithoutExtension(entityMarkdownPath));
	}

	/// <summary>
	/// Writes an asset into an asset folder, returning the stored file name. The watcher is suppressed around
	/// the write so the core's own file creation is not mistaken for an external change.
	/// </summary>
	public async Task<string> StoreAsync(string assetFolder, string fileName, byte[] bytes, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(assetFolder);
		ArgumentNullException.ThrowIfNull(bytes);
		var safeName = SanitizeFileName(fileName);
		if (bytes.LongLength == 0)
		{
			throw new InvalidOperationException("Refusing to store an empty media asset.");
		}

		if (bytes.LongLength > MaxAssetBytes)
		{
			throw new InvalidOperationException($"Media asset '{safeName}' exceeds the {MaxAssetBytes}-byte limit.");
		}

		Directory.CreateDirectory(assetFolder);
		var target = Path.Combine(assetFolder, safeName);
		writeBarrier.Suppress(target);
		await File.WriteAllBytesAsync(target, bytes, cancellationToken);
		return safeName;
	}

	/// <summary>
	/// Resolves a stored file name to a forward-slashed vault-relative path (Obsidian path form), or
	/// <see langword="null"/> when nothing is stored or the file is missing.
	/// </summary>
	public string? ResolveVaultRelative(string assetFolder, string? fileName)
	{
		if (string.IsNullOrWhiteSpace(assetFolder) || string.IsNullOrWhiteSpace(fileName))
		{
			return null;
		}

		var full = Path.Combine(assetFolder, fileName);
		if (!File.Exists(full))
		{
			return null;
		}

		return Path.GetRelativePath(layout.VaultRoot, full).Replace(Path.DirectorySeparatorChar, '/');
	}

	/// <summary>
	/// Archives a stored asset into the file graveyard (recoverable and audited) rather than deleting it.
	/// A missing file is a no-op.
	/// </summary>
	public async Task DeleteAsync(
		string assetFolder,
		string fileName,
		string reason,
		string? entityType = null,
		string? entityId = null,
		string? entityTitle = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(assetFolder) || string.IsNullOrWhiteSpace(fileName))
		{
			return;
		}

		var target = Path.Combine(assetFolder, fileName);
		if (!File.Exists(target))
		{
			return;
		}

		writeBarrier.Suppress(target);
		await temporalDataService.ArchivePathAsync(target, reason, entityType, entityId, entityTitle, cancellationToken: cancellationToken);
	}

	/// <summary>Lists the file names stored in an asset folder, or an empty list when it does not exist.</summary>
	public IReadOnlyList<string> List(string assetFolder)
	{
		if (string.IsNullOrWhiteSpace(assetFolder) || !Directory.Exists(assetFolder))
		{
			return [];
		}

		return Directory.EnumerateFiles(assetFolder)
			.Select(Path.GetFileName)
			.Where(name => !string.IsNullOrEmpty(name))
			.Select(name => name!)
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	/// <summary>
	/// Resolves a raw media key into a <see cref="MediaReference"/>: a <c>vault:</c> file against the shared
	/// vault asset folder, a <c>media:</c> file against <paramref name="selfAssetFolder"/>, or a schemeless
	/// glyph name. Returns <see langword="null"/> for an empty key.
	/// </summary>
	public MediaReference? ResolveReference(string? key, string? selfAssetFolder)
	{
		if (string.IsNullOrWhiteSpace(key))
		{
			return null;
		}

		if (key.StartsWith(VaultReferencePrefix, StringComparison.Ordinal))
		{
			return new MediaReference(key, MediaKind.Vault, ResolveVaultRelative(VaultAssetFolder, key[VaultReferencePrefix.Length..]));
		}

		if (key.StartsWith(SelfReferencePrefix, StringComparison.Ordinal))
		{
			var file = key[SelfReferencePrefix.Length..];
			return new MediaReference(key, MediaKind.Media, selfAssetFolder is null ? null : ResolveVaultRelative(selfAssetFolder, file));
		}

		return new MediaReference(key, MediaKind.Icon, null);
	}

	/// <summary>
	/// Fills the <see cref="MediaReference"/> companion of every <see cref="MediaAttribute"/> key on an entity
	/// (PEP105). <paramref name="selfAssetFolder"/> is the entity's own asset folder, needed only to resolve
	/// <c>media:</c> keys; pass <see langword="null"/> when the entity has none (see <see cref="HasSelfMedia"/>).
	/// </summary>
	public void EnrichMedia(object entity, string? selfAssetFolder)
	{
		ArgumentNullException.ThrowIfNull(entity);
		foreach (var (keyProperty, companionProperty) in GetMediaProperties(entity.GetType()))
		{
			companionProperty.SetValue(entity, ResolveReference(keyProperty.GetValue(entity) as string, selfAssetFolder));
		}
	}

	/// <summary>
	/// Whether an entity has any self (<c>media:</c>) key, and so needs its own asset folder resolved before
	/// enrichment. Vault keys and glyphs resolve without it.
	/// </summary>
	public bool HasSelfMedia(object entity)
	{
		ArgumentNullException.ThrowIfNull(entity);
		foreach (var (keyProperty, _) in GetMediaProperties(entity.GetType()))
		{
			if (keyProperty.GetValue(entity) is string value && value.StartsWith(SelfReferencePrefix, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Splits a raw key into how it resolves and the file name it points at (<see langword="null"/> for a glyph).
	/// </summary>
	public static (MediaKind Kind, string? File) ParseKey(string? key)
	{
		if (string.IsNullOrWhiteSpace(key))
		{
			return (MediaKind.Icon, null);
		}

		if (key.StartsWith(VaultReferencePrefix, StringComparison.Ordinal))
		{
			return (MediaKind.Vault, key[VaultReferencePrefix.Length..]);
		}

		if (key.StartsWith(SelfReferencePrefix, StringComparison.Ordinal))
		{
			return (MediaKind.Media, key[SelfReferencePrefix.Length..]);
		}

		return (MediaKind.Icon, null);
	}

	/// <summary>Wraps a stored file name as a self (<c>media:</c>) key.</summary>
	public static string ToSelfReference(string fileName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
		return SelfReferencePrefix + fileName;
	}

	/// <summary>Wraps a stored file name as a vault (<c>vault:</c>) key.</summary>
	public static string ToVaultReference(string fileName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
		return VaultReferencePrefix + fileName;
	}

	private static IReadOnlyList<(PropertyInfo Key, PropertyInfo Companion)> GetMediaProperties(Type type)
	{
		return MediaPropertyCache.GetOrAdd(type, static entityType =>
		{
			var pairs = new List<(PropertyInfo, PropertyInfo)>();
			foreach (var property in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
			{
				if (property.GetCustomAttribute<MediaAttribute>() is null)
				{
					continue;
				}

				var companionName = property.Name + "Media";
				var companion = entityType.GetProperty(companionName)
					?? throw new InvalidOperationException($"[Media] property '{entityType.Name}.{property.Name}' requires a companion '{companionName}' property.");
				var companionType = Nullable.GetUnderlyingType(companion.PropertyType) ?? companion.PropertyType;
				if (!typeof(MediaReference).IsAssignableFrom(companionType))
				{
					throw new InvalidOperationException($"Companion '{entityType.Name}.{companion.Name}' must be of type {nameof(MediaReference)}.");
				}

				pairs.Add((property, companion));
			}

			return pairs;
		});
	}

	private static string SanitizeFileName(string fileName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
		var name = Path.GetFileName(fileName.Trim());
		if (string.IsNullOrWhiteSpace(name)
			|| name is "." or ".."
			|| name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
		{
			throw new InvalidOperationException($"Media file name '{fileName}' is not a safe single file name.");
		}

		var extension = Path.GetExtension(name);
		if (!AllowedExtensions.Contains(extension))
		{
			throw new InvalidOperationException($"Media file extension '{extension}' is not an allowed image type.");
		}

		return name;
	}
}
