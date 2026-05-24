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
	/// If any errors occur in parsing the properties from files, those values will be overriden by the core.
	/// </summary>
	Synced,

	/// <summary>
	/// Entities wil be created by the core with a default file for them,
	/// property changes between them will be synced, but removals will not be synced by either side.
	/// <br/>
	/// If any errors occur in parsing the properties from files, those values will be overriden by the core.
	/// </summary>
	Optional,

	/// <summary>
	/// Entities will be created by files,
	/// the core will only keep track of them and their properties, including removals.
	/// <br/>
	/// If any errors occur in parsing the properties from files, those values will be overriden by the core.
	/// </summary>
	FileFirst,

	/// <summary>
	/// Entities will be created by the core with a default file for them,
	/// properties and the existence of the file will be enforced by the core.
	/// Files that are not in the database will be purged.
	/// </summary>
	Enforced,
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