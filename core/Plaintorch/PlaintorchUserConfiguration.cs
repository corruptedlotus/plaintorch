using System.Text.Json;
using Pleiades.Vault;

namespace Pleiades.Plaintorch;

/// <summary>
/// Resolves the per-user PLAINTORCH host paths used for shared runtime configuration and the service socket.
/// </summary>
public sealed class PlaintorchUserLayout
{
	/// <summary>
	/// The name of the static predefined OS account that interactive/manual <c>serve</c> runs as during development.
	/// End users run PLAINTORCH through the installed service runner instead, which uses the real per-user environment.
	/// </summary>
	public const string DevUserName = "PLAINTORCHDEV";

	/// <summary>
	/// The environment variable that overrides the development account password used to relaunch manual <c>serve</c>.
	/// </summary>
	public const string DevPasswordEnvironmentVariable = "PLAINTORCHDEV_PASSWORD";

	/// <summary>
	/// The documented default development account password. This is a deliberately static, dev-only value shared with the
	/// setup scripts for an unprivileged local account; override it through <see cref="DevPasswordEnvironmentVariable"/>.
	/// </summary>
	public const string DefaultDevPassword = "Plaintorch-Dev-Local-1";

	private readonly bool _ephemeral;

	private PlaintorchUserLayout(string rootPath, bool ephemeral = false)
	{
		RootPath = rootPath;
		_ephemeral = ephemeral;
	}

	/// <summary>
	/// Gets the root directory that stores per-user PLAINTORCH host state.
	/// </summary>
	public string RootPath { get; }

	/// <summary>
	/// Gets or sets a value indicating whether the fixed loopback HTTP endpoint should be bound in addition to the socket.
	/// Socket-only is the default; the loopback endpoint is opt-in.
	/// </summary>
	public bool LoopbackEnabled { get; set; }

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
	/// Gets a value indicating whether this layout is an ephemeral, user-independent development profile.
	/// </summary>
	public bool IsEphemeral => _ephemeral;

	/// <summary>
	/// Resolves the development account password used to relaunch manual <c>serve</c>, honoring the environment override.
	/// </summary>
	/// <returns>The resolved development password.</returns>
	public static string ResolveDevPassword()
	{
		var overridden = Environment.GetEnvironmentVariable(DevPasswordEnvironmentVariable);
		return string.IsNullOrEmpty(overridden) ? DefaultDevPassword : overridden;
	}

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
	/// Creates a per-user host layout rooted at an explicit directory.
	/// </summary>
	/// <param name="rootPath">The root directory that should hold per-user host state.</param>
	/// <returns>The resolved host layout.</returns>
	public static PlaintorchUserLayout CreateAt(string rootPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
		return new PlaintorchUserLayout(Path.GetFullPath(rootPath));
	}

	/// <summary>
	/// Creates a randomised, user-independent ephemeral host layout under the OS temporary directory.
	/// Used by tests and throwaway sandbox runs so multiple instances never collide on the socket, config, or port.
	/// </summary>
	/// <returns>The resolved ephemeral host layout.</returns>
	public static PlaintorchUserLayout CreateEphemeral()
	{
		var root = Path.Combine(Path.GetTempPath(), "plaintorch-dev", Guid.NewGuid().ToString("N"));
		return new PlaintorchUserLayout(root, ephemeral: true);
	}

	/// <summary>
	/// Ensures the per-user host root directory exists.
	/// </summary>
	public void EnsureExists()
	{
		Directory.CreateDirectory(RootPath);
	}

	/// <summary>
	/// Removes an ephemeral host root and its contents. No-op for non-ephemeral layouts.
	/// </summary>
	public void Cleanup()
	{
		if (!_ephemeral || !Directory.Exists(RootPath))
		{
			return;
		}

		try
		{
			Directory.Delete(RootPath, recursive: true);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
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