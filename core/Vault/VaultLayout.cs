namespace Pleiades.Vault;

/// <summary>
/// Calculates canonical filesystem locations used by PLAINTORCH inside a vault.
/// </summary>
/// <remarks>
/// The hosted core can run without a bound vault (idle daemon mode). The layout therefore holds a
/// swappable <see cref="Binding"/> that the activation coordinator sets when a vault is activated and
/// clears when it is deactivated. While unbound, every location accessor raises
/// <see cref="VaultNotActiveException"/> so idle vault-scoped work fails fast instead of computing a
/// path against a non-existent vault root.
/// </remarks>
public sealed class VaultLayout
{
	private volatile Binding? _binding;

	/// <summary>
	/// Initializes an unbound layout for daemon mode, where a vault is activated later through user settings.
	/// </summary>
	public VaultLayout()
	{
	}

	/// <summary>
	/// Initializes a layout bound to a fixed vault, used by single-command and design-time flows.
	/// </summary>
	/// <param name="options">The vault settings to apply.</param>
	public VaultLayout(VaultOptions options)
	{
		Bind(options);
	}

	/// <summary>
	/// Gets a value indicating whether a vault is currently bound to this layout.
	/// </summary>
	public bool IsBound => _binding is not null;

	/// <summary>
	/// Binds the layout to a vault, loading its on-disk settings.
	/// </summary>
	/// <param name="options">The vault settings to apply.</param>
	public void Bind(VaultOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		_binding = new Binding(options);
	}

	/// <summary>
	/// Clears the currently bound vault, returning the layout to its idle state.
	/// </summary>
	public void Unbind()
	{
		_binding = null;
	}

	/// <summary>
	/// Gets the absolute root path of the vault.
	/// </summary>
	public string VaultRoot => Current.VaultRoot;

	/// <summary>
	/// Gets the absolute path of the vault settings document.
	/// </summary>
	public string SettingsPath => Path.Combine(Current.VaultRoot, Current.Options.SettingsFileName);

	/// <summary>
	/// Gets the absolute path of the active service lock document.
	/// </summary>
	public string LockPath => Path.Combine(Current.VaultRoot, Current.Options.LockFileName);

	/// <summary>
	/// Gets the absolute path of the PLAINTORCH metadata directory.
	/// </summary>
	public string MetadataRoot => Path.Combine(Current.VaultRoot, Current.Options.MetadataDirectoryName);

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
	public string DatabasePath => Path.Combine(MetadataRoot, Current.Options.DatabaseFileName);

	/// <summary>
	/// Gets the canonical directives root directory.
	/// </summary>
	public string DirectivesRoot => GetLocationRoot(VaultLocationKeys.Directives);

	/// <summary>
	/// Gets the canonical lunar (Moonlight) directives root directory (PEP100).
	/// </summary>
	public string MoonlightRoot => GetLocationRoot(VaultLocationKeys.Moonlight);

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
		var binding = Current;
		return Path.Combine(binding.VaultRoot, binding.Settings.ResolveLocation(locationKey));
	}

	/// <summary>
	/// Ensures the vault settings file exists with default values when absent.
	/// </summary>
	public void EnsureSettingsFile()
	{
		if (!File.Exists(SettingsPath))
		{
			Current.Settings.Save(SettingsPath);
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
		yield return MoonlightRoot;
		yield return OnrushRoot;
		yield return ObjectivesRoot;
		yield return FatesRoot;
		yield return DecreesRoot;
		yield return JournalRoot;
		yield return SagaRoot;
	}

	private Binding Current => _binding ?? throw new VaultNotActiveException();

	/// <summary>
	/// Immutable snapshot of a bound vault and its resolved on-disk settings.
	/// </summary>
	private sealed class Binding
	{
		public Binding(VaultOptions options)
		{
			Options = options;
			VaultRoot = Path.GetFullPath(options.VaultPath);
			Settings = VaultSettings.Load(Path.Combine(VaultRoot, options.SettingsFileName));
		}

		public VaultOptions Options { get; }

		public VaultSettings Settings { get; }

		public string VaultRoot { get; }
	}
}
