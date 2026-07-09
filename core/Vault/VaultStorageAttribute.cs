namespace Pleiades.Vault;

/// <summary>
/// Indicates how entities of a type are stored as files in the vault.
/// </summary>
public enum VaultStorageMode
{
	/// <summary>
	/// Entities can be created by both files and the core,
	/// property changes between them will be synced, removals will also be synced from both sides.
	/// <br/>
	/// If any errors occur in parsing the properties from files, those values will be overridden by the core.
	/// </summary>
	Synced,

	/// <summary>
	/// Entities will be created by the core with a default file for them,
	/// property changes between them will be synced, but removals will not be synced by either side.
	/// <br/>
	/// If any errors occur in parsing the properties from files, those values will be overridden by the core.
	/// </summary>
	Optional,

	/// <summary>
	/// Entities will be created by files,
	/// the core will only keep track of them and their properties, including removals.
	/// <br/>
	/// If any errors occur in parsing the properties from files, those values will be overridden by the core.
	/// </summary>
	FileFirst,

	/// <summary>
	/// Entities will be created by the core with a default file for them,
	/// properties and the existence of the file will be enforced by the core.
	/// Files that are not in the database will be purged.
	/// </summary>
	Enforced,

	/// <summary>
	/// Entities can be stored anywhere in the vault while keeping canonical core-authored defaults.
	/// PUCK identity is expected from frontmatter instead of filename composition.
	/// </summary>
	Freeform,

	/// <summary>
	/// Entities behave identically to <see cref="Freeform"/> but do not initially materialize a file.
	/// An implicit entity begins syncing only after its file starts existing (through any means), at which point a
	/// synchronization boundary is recorded and subsequent deletions of that file become authoritative upstream.
	/// This is the conceptual opposite of freeform upstream initialization.
	/// </summary>
	Implicit,
}

/// <summary>
/// Indicates how the PUCK identity of a vault-backed entity is persisted alongside its file.
/// </summary>
public enum VaultPuckStorage
{
	/// <summary>
	/// Stores the entity identity as a <c>puck</c> frontmatter field while keeping a title-only filename.
	/// This is the default PUCK storage form.
	/// </summary>
	Quiet,

	/// <summary>
	/// Stores the entity identity as part of the filename using the canonical <c>{PUCK token} - {Title}</c> convention.
	/// </summary>
	Index,
}

/// <summary>
/// Provides shared classification helpers over <see cref="VaultStorageMode"/> values.
/// </summary>
public static class VaultStorageModeExtensions
{
	/// <summary>
	/// Determines whether a storage mode resolves ownership from frontmatter PUCK identity rather than path shape.
	/// Both <see cref="VaultStorageMode.Freeform"/> and <see cref="VaultStorageMode.Implicit"/> are identity-driven.
	/// </summary>
	/// <param name="mode">The storage mode to classify.</param>
	/// <returns><see langword="true"/> when the mode is identity-driven; otherwise, <see langword="false"/>.</returns>
	public static bool IsIdentityDriven(this VaultStorageMode mode)
	{
		return mode is VaultStorageMode.Freeform or VaultStorageMode.Implicit;
	}
}

/// <summary>
/// Indicates the physical filesystem shape used for a vault-backed entity.
/// </summary>
public enum VaultStorageShape
{
	/// <summary>
	/// Stores the entity as a self-named directory containing a same-name markdown file.
	/// </summary>
	SelfNamedDirectory,

	/// <summary>
	/// Stores the entity as a single markdown file.
	/// </summary>
	SingleFile,
}

/// <summary>
/// Indicates that this type is reflected in vault files.
/// Types without this attribute are treated as database-only by default.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class VaultStorageAttribute : Attribute
{
	/// <summary>
	/// Gets the logical layout key used to resolve the entity's storage location.
	/// </summary>
	public required string LocationKey { get; init; }

	/// <summary>
	/// Gets the synchronization mode applied to the vault-backed entity.
	/// </summary>
	public VaultStorageMode Mode { get; init; } = VaultStorageMode.Synced;

	/// <summary>
	/// Gets the physical filesystem shape used by the entity.
	/// </summary>
	public VaultStorageShape Shape { get; init; } = VaultStorageShape.SingleFile;

	/// <summary>
	/// Gets the PUCK storage form used to persist the entity identity.
	/// Defaults to <see cref="VaultPuckStorage.Quiet"/>; models requiring filename-embedded identity must opt into
	/// <see cref="VaultPuckStorage.Index"/> explicitly.
	/// </summary>
	public VaultPuckStorage PuckStorage { get; init; } = VaultPuckStorage.Quiet;

	/// <summary>
	/// Gets the optional property name containing the parent entity identifier used for composed storage paths.
	/// </summary>
	public string? ParentIdProperty { get; init; }

	/// <summary>
	/// Gets the optional parent entity CLR type used for composed storage paths.
	/// </summary>
	public Type? ParentEntityType { get; init; }

	/// <summary>
	/// Gets the optional property name containing a vault-relative parent directory override.
	/// </summary>
	public string? ParentDirectoryProperty { get; init; }

	/// <summary>
	/// Gets the optional subdirectory name used when storing this entity beneath a resolved parent directory.
	/// </summary>
	public string? PartitionUnder { get; init; }
}