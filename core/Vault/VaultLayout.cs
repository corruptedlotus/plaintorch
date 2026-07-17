namespace Pleiades.Vault;

/// <summary>
/// Calculates canonical filesystem locations used by PLAINTORCH inside a vault.
/// </summary>
/// <param name="options">The vault settings to apply.</param>
public sealed class VaultLayout(VaultOptions options)
{
	private readonly VaultOptions _options = options;
	private readonly VaultSettings _settings = VaultSettings.Load(Path.Combine(Path.GetFullPath(options.VaultPath), options.SettingsFileName));

	/// <summary>
	/// Gets the absolute root path of the vault.
	/// </summary>
	public string VaultRoot { get; } = Path.GetFullPath(options.VaultPath);

	/// <summary>
	/// Gets the absolute path of the vault settings document.
	/// </summary>
	public string SettingsPath => Path.Combine(VaultRoot, _options.SettingsFileName);

	/// <summary>
	/// Gets the absolute path of the active service lock document.
	/// </summary>
	public string LockPath => Path.Combine(VaultRoot, _options.LockFileName);

	/// <summary>
	/// Gets the absolute path of the PLAINTORCH metadata directory.
	/// </summary>
	public string MetadataRoot => Path.Combine(VaultRoot, _options.MetadataDirectoryName);

	/// <summary>
	/// Gets the absolute path of the graveyard root under vault metadata.
	/// </summary>
	public string GraveyardRoot => Path.Combine(MetadataRoot, "graveyard");

	/// <summary>
	/// Gets the absolute path of the file graveyard root under vault metadata.
	/// </summary>
	public string FileGraveyardRoot => Path.Combine(GraveyardRoot, "files");

	/// <summary>
	/// Gets the absolute path of the vault-local SQLite database file.
	/// </summary>
	public string DatabasePath => Path.Combine(MetadataRoot, _options.DatabaseFileName);

	/// <summary>
	/// Gets the canonical directives root directory.
	/// </summary>
	public string DirectivesRoot => GetLocationRoot(VaultLocationKeys.Directives);

	/// <summary>
	/// Gets the canonical onrush root directory.
	/// </summary>
	public string OnrushRoot => GetLocationRoot(VaultLocationKeys.Onrush);

	/// <summary>
	/// Gets the canonical standalone objectives root directory.
	/// </summary>
	public string ObjectivesRoot => GetLocationRoot(VaultLocationKeys.Objectives);

	/// <summary>
	/// Gets the canonical standalone fates root directory (PEP100).
	/// </summary>
	public string FatesRoot => GetLocationRoot(VaultLocationKeys.Fates);

	/// <summary>
	/// Gets the canonical standalone decrees root directory (PEP100).
	/// </summary>
	public string DecreesRoot => GetLocationRoot(VaultLocationKeys.Decrees);

	/// <summary>
	/// Gets the canonical journal root directory.
	/// </summary>
	public string JournalRoot => GetLocationRoot(VaultLocationKeys.Journal);

	/// <summary>
	/// Gets the canonical saga root directory.
	/// </summary>
	public string SagaRoot => GetLocationRoot(VaultLocationKeys.Saga);

	/// <summary>
	/// Resolves the absolute root directory for a logical location key.
	/// </summary>
	/// <param name="locationKey">The logical location key.</param>
	/// <returns>The absolute directory path.</returns>
	public string GetLocationRoot(string locationKey)
	{
		return Path.Combine(VaultRoot, _settings.ResolveLocation(locationKey));
	}

	/// <summary>
	/// Ensures the vault settings file exists with default values when absent.
	/// </summary>
	public void EnsureSettingsFile()
	{
		if (!File.Exists(SettingsPath))
		{
			_settings.Save(SettingsPath);
		}
	}

	/// <summary>
	/// Enumerates the directories that should exist for a usable vault layout.
	/// </summary>
	/// <returns>The required directory paths.</returns>
	public IEnumerable<string> GetRequiredDirectories()
	{
		yield return VaultRoot;
		yield return MetadataRoot;
		yield return GraveyardRoot;
		yield return FileGraveyardRoot;
		yield return DirectivesRoot;
		yield return OnrushRoot;
		yield return ObjectivesRoot;
		yield return FatesRoot;
		yield return DecreesRoot;
		yield return JournalRoot;
		yield return SagaRoot;
	}
}