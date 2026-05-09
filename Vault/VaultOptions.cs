namespace Pleiades.Vault;

/// <summary>
/// Holds user-configurable settings for the vault-backed PLAINTORCH workspace.
/// </summary>
public sealed class VaultOptions
{
	/// <summary>
	/// The default filename used for the SQLite database stored in the vault metadata directory.
	/// </summary>
	public const string DefaultDatabaseFileName = "plaintorch.sqlite3";

	/// <summary>
	/// The default metadata directory name created inside the vault root.
	/// </summary>
	public const string DefaultMetadataDirectoryName = ".plaintorch-data";

	/// <summary>
	/// The default filename used for the vault-local PLAINTORCH settings document.
	/// </summary>
	public const string DefaultSettingsFileName = ".plaintorch";

	/// <summary>
	/// The default filename used for the active service lock document inside the vault root.
	/// </summary>
	public const string DefaultLockFileName = ".plaintorch.lock";

	/// <summary>
	/// Gets the root path of the Obsidian vault.
	/// </summary>
	public required string VaultPath { get; init; }

	/// <summary>
	/// Gets the filename used for the SQLite database stored in the vault metadata directory.
	/// </summary>
	public string DatabaseFileName { get; init; } = DefaultDatabaseFileName;

	/// <summary>
	/// Gets the name of the metadata directory created inside the vault root.
	/// </summary>
	public string MetadataDirectoryName { get; init; } = DefaultMetadataDirectoryName;

	/// <summary>
	/// Gets the filename used for the vault-local PLAINTORCH settings document.
	/// </summary>
	public string SettingsFileName { get; init; } = DefaultSettingsFileName;

	/// <summary>
	/// Gets the filename used for the active service lock document inside the vault root.
	/// </summary>
	public string LockFileName { get; init; } = DefaultLockFileName;
}