using System.Text.Json;
using Pleiades.Vault;

namespace Pleiades.Plaintorch;

/// <summary>
/// Resolves the per-user PLAINTORCH host paths used for shared runtime configuration and the service socket.
/// </summary>
public sealed class PlaintorchUserLayout
{
	private readonly bool _ephemeral;
	private readonly bool _devProfile;

	private PlaintorchUserLayout(string rootPath, bool ephemeral = false, bool devProfile = false)
	{
		RootPath = rootPath;
		_ephemeral = ephemeral;
		_devProfile = devProfile;
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
	/// Gets the Windows named-pipe name for the core IPC endpoint. On Windows a Node client's socket path resolves to
	/// a named pipe rather than an AF_UNIX socket, so the host binds this pipe and clients connect to it instead of
	/// <see cref="SocketPath"/>. The name is per-profile and per-user so dev/real profiles and separate users never
	/// collide on the machine-global pipe namespace (Windows pipe names match case-insensitively).
	/// </summary>
	public string PipeName => $"{Path.GetFileName(RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}.{Environment.UserName}";

	/// <summary>
	/// Gets the transport endpoint clients connect to on the current platform: the Windows named pipe, or the
	/// AF_UNIX socket path elsewhere.
	/// </summary>
	public string EndpointDisplay => OperatingSystem.IsWindows() ? $@"\\.\pipe\{PipeName}" : SocketPath;

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
	/// Gets a value indicating whether this layout is the current user's persistent development sub-profile.
	/// Manual <c>serve</c> runs against this isolated sub-profile so a developer sandbox never collides with the real
	/// per-user daemon; unlike an ephemeral profile it persists between runs and is never auto-removed.
	/// </summary>
	public bool IsDevProfile => _devProfile;

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
	/// Creates the current user's persistent development sub-profile layout under <c>~/.pleiades/plaintorch-dev</c>.
	/// Ordinary manual <c>serve</c> runs use this isolated-but-persistent sub-profile instead of the real per-user
	/// profile, so a developer sandbox keeps its own config/socket/port across runs without touching a real installed
	/// daemon and without provisioning a separate OS account.
	/// </summary>
	/// <returns>The resolved development sub-profile layout.</returns>
	public static PlaintorchUserLayout CreateDevProfile()
	{
		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		return new PlaintorchUserLayout(Path.Combine(home, ".pleiades", "plaintorch-dev"), devProfile: true);
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

	/// <summary>
	/// Clears the active per-user PLAINTORCH vault so the hosted core returns to idle.
	/// </summary>
	/// <returns>The vault path that was previously active, or <see langword="null"/> when none was configured.</returns>
	public string? Deactivate()
	{
		var configuration = configurationStore.Load();
		var previousVaultPath = configuration.ActiveVaultPath;
		if (previousVaultPath is null)
		{
			return null;
		}

		configuration.ActiveVaultPath = null;
		configurationStore.Save(configuration);
		return previousVaultPath;
	}
}