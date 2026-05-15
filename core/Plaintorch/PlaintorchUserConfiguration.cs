using System.Text.Json;
using Pleiades.Vault;

namespace Pleiades.Plaintorch;

/// <summary>
/// Resolves the per-user PLAINTORCH host paths used for shared runtime configuration and the service socket.
/// </summary>
public sealed class PlaintorchUserLayout
{
	private PlaintorchUserLayout(string rootPath)
	{
		RootPath = rootPath;
	}

	/// <summary>
	/// Gets the root directory that stores per-user PLAINTORCH host state.
	/// </summary>
	public string RootPath { get; }

	/// <summary>
	/// Gets the path of the shared per-user host configuration file.
	/// </summary>
	public string ConfigurationPath => Path.Combine(RootPath, "config.json");

	/// <summary>
	/// Gets the path of the per-user socket file that future clients will connect to.
	/// </summary>
	public string SocketPath => Path.Combine(RootPath, "plaintorch.sock");

	/// <summary>
	/// Gets the loopback HTTP port exposed for desktop integrations that cannot reliably use the socket transport.
	/// </summary>
	public int LoopbackPort => 43118;

	/// <summary>
	/// Gets the loopback HTTP base URL exposed for desktop integrations.
	/// </summary>
	public string LoopbackBaseUrl => $"http://127.0.0.1:{LoopbackPort}";

	/// <summary>
	/// Gets the directory that stores optional splash-screen assets and runtime state.
	/// </summary>
	public string SplashRootPath => Path.Combine(RootPath, "splash");

	/// <summary>
	/// Gets the runtime state file path used by the interactive splash popup.
	/// </summary>
	public string SplashStatePath => Path.Combine(SplashRootPath, "state.json");

	/// <summary>
	/// Gets the generated PowerShell script path used to render the interactive splash popup.
	/// </summary>
	public string SplashScriptPath => Path.Combine(SplashRootPath, "plaintorch-core-splash.ps1");

	/// <summary>
	/// Creates the default per-user PLAINTORCH host layout for the current platform.
	/// </summary>
	/// <returns>The resolved host layout.</returns>
	public static PlaintorchUserLayout CreateDefault()
	{
		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		return new PlaintorchUserLayout(Path.Combine(home, ".pleiades", "plaintorch"));
	}

	/// <summary>
	/// Ensures the per-user host root directory exists.
	/// </summary>
	public void EnsureExists()
	{
		Directory.CreateDirectory(RootPath);
	}
}

/// <summary>
/// Represents the per-user PLAINTORCH host configuration stored outside the vault.
/// </summary>
public sealed class PlaintorchUserConfiguration
{
	/// <summary>
	/// Gets or sets the currently active PLAINTORCH vault path.
	/// </summary>
	public string? ActiveVaultPath { get; set; }
}

/// <summary>
/// Loads and saves the per-user PLAINTORCH host configuration.
/// </summary>
public sealed class PlaintorchUserConfigurationStore(PlaintorchUserLayout layout)
{
	private static readonly JsonSerializerOptions SerializerOptions = new()
	{
		WriteIndented = true,
	};

	/// <summary>
	/// Loads the current per-user configuration.
	/// </summary>
	/// <returns>The stored configuration, or a new empty configuration when none exists.</returns>
	public PlaintorchUserConfiguration Load()
	{
		if (!File.Exists(layout.ConfigurationPath))
		{
			return new PlaintorchUserConfiguration();
		}

		var json = File.ReadAllText(layout.ConfigurationPath);
		return JsonSerializer.Deserialize<PlaintorchUserConfiguration>(json, SerializerOptions)
			?? new PlaintorchUserConfiguration();
	}

	/// <summary>
	/// Saves the per-user configuration.
	/// </summary>
	/// <param name="configuration">The configuration to persist.</param>
	public void Save(PlaintorchUserConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		layout.EnsureExists();
		var json = JsonSerializer.Serialize(configuration, SerializerOptions);
		File.WriteAllText(layout.ConfigurationPath, json + Environment.NewLine);
	}
}

/// <summary>
/// Manages activation of the singular per-user PLAINTORCH vault.
/// </summary>
public sealed class PlaintorchVaultActivationService(
	PlaintorchUserLayout userLayout,
	PlaintorchUserConfigurationStore configurationStore)
{
	/// <summary>
	/// Gets the currently active vault path, when one has been configured.
	/// </summary>
	/// <returns>The active vault path, or <see langword="null"/> when none is configured.</returns>
	public string? GetActiveVaultPath()
	{
		return configurationStore.Load().ActiveVaultPath;
	}

	/// <summary>
	/// Determines whether a directory already looks like an initialized PLAINTORCH vault.
	/// </summary>
	/// <param name="vaultPath">The vault directory to inspect.</param>
	/// <returns><see langword="true"/> when the directory contains PLAINTORCH vault settings; otherwise, <see langword="false"/>.</returns>
	public bool IsInitialized(string vaultPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(vaultPath);
		var fullPath = Path.GetFullPath(vaultPath);
		return File.Exists(Path.Combine(fullPath, VaultOptions.DefaultSettingsFileName));
	}

	/// <summary>
	/// Marks a vault as the active per-user PLAINTORCH vault.
	/// </summary>
	/// <param name="vaultPath">The vault path to activate.</param>
	/// <returns>The normalized active vault path.</returns>
	public string Activate(string vaultPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(vaultPath);
		var fullPath = Path.GetFullPath(vaultPath);
		if (!IsInitialized(fullPath))
		{
			throw new InvalidOperationException($"Vault '{fullPath}' is not initialized for PLAINTORCH. Run 'init' in that directory first.");
		}

		var configuration = configurationStore.Load();
		configuration.ActiveVaultPath = fullPath;
		configurationStore.Save(configuration);
		userLayout.EnsureExists();
		return fullPath;
	}
}