namespace Pleiades.Vault;

/// <summary>
/// Reads and advances the vault schema version persisted in the vault-local settings document.
/// </summary>
public sealed class VaultVersionService(VaultLayout layout)
{
	/// <summary>
	/// Gets the vault schema version the current engine emits.
	/// </summary>
	public int CurrentVersion => VaultSchema.CurrentVersion;

	/// <summary>
	/// Reads the vault schema version currently stored in the vault settings document.
	/// A settings document without a stored version resolves to <see cref="VaultSchema.BaselineVersion"/>.
	/// </summary>
	public int GetStoredVersion()
	{
		return VaultSettings.Load(layout.SettingsPath).SchemaVersion;
	}

	/// <summary>
	/// Persists a new vault schema version to the vault settings document, preserving other settings.
	/// </summary>
	/// <param name="version">The version to record.</param>
	public void SetStoredVersion(int version)
	{
		var settings = VaultSettings.Load(layout.SettingsPath);
		settings.SchemaVersion = version;
		settings.Save(layout.SettingsPath);
	}
}
