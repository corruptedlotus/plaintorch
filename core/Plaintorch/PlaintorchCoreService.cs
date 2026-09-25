using Pleiades.Plaintorch.Hosting;
using Pleiades.Plaintorch.Materialization;
using Pleiades.Vault;
using Pleiades.Puck;

namespace Pleiades.Plaintorch;

/// <summary>
/// Provides the long-running hosted core process used by service managers on Windows and Linux.
/// </summary>
/// <remarks>
/// The core starts in daemon mode without a vault. It resolves the active vault from user settings and,
/// while none is configured, waits idle. When a vault is activated (through <c>plaintorch activate</c>,
/// which writes the active vault into user settings), the coordinator binds the vault layout, acquires the
/// vault lock, initializes vault state, and marks the session ready so vault-scoped services begin serving.
/// When the vault is deactivated it releases the vault, purges cached vault state, and returns to idle until
/// another vault is activated.
/// </remarks>
public sealed class PlaintorchCoreService(
	IServiceScopeFactory scopeFactory,
	ILogger<PlaintorchCoreService> logger,
	PlaintorchUserLayout userLayout,
	PlaintorchUserConfigurationStore configurationStore,
	PlaintorchVaultActivationService activationService,
	VaultLayout layout,
	ActiveVaultSession session,
	PlaintorchVaultLockService lockService,
	PlaintorchHostState hostState,
	PuckRuntimeCompilationCatalog puckRuntimeCompilationCatalog,
	TimeframeCandidateCache timeframeCandidateCache) : BackgroundService
{
	private static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(2);

	/// <summary>How long the sweep flag may stay set with no word from the watcher before the coordinator clears it.</summary>
	private static readonly TimeSpan SweepBackstopTimeout = TimeSpan.FromSeconds(30);

	private readonly SemaphoreSlim _reconcileSignal = new(0, int.MaxValue);
	private string? _activeVaultPath;
	private string? _failedVaultPath;
	private PlaintorchVaultLockHandle? _lockHandle;

	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		userLayout.EnsureExists();
		logger.LogInformation("PLAINTORCH core started in idle daemon mode. Watching user settings for an active vault.");
		hostState.Report(PlaintorchHostPhase.Idle, "Waiting for a vault to be activated.");

		using var configurationWatcher = CreateConfigurationWatcher();

		try
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				await ReconcileAsync(stoppingToken);
				await WaitForNextReconcileAsync(stoppingToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			await DeactivateAsync();
		}
	}

	/// <summary>
	/// Reconciles the currently served vault with the vault configured in user settings.
	/// </summary>
	private async Task ReconcileAsync(CancellationToken stoppingToken)
	{
		var desiredVaultPath = ResolveDesiredVaultPath();

		if (string.Equals(desiredVaultPath, _activeVaultPath, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		if (_activeVaultPath is not null)
		{
			logger.LogInformation("Active vault changed in user settings. Releasing '{VaultPath}'.", _activeVaultPath);
			await DeactivateAsync();
		}

		if (desiredVaultPath is null)
		{
			// Either nothing is configured or the configured vault is not servable; ResolveDesiredVaultPath
			// has already logged and recorded that so it is not repeated every reconcile.
			logger.LogInformation("No servable vault configured. PLAINTORCH core is idle and waiting for activation.");
			if (_failedVaultPath is null)
			{
				hostState.Report(PlaintorchHostPhase.Idle, "Waiting for a vault to be activated.");
			}

			return;
		}

		if (string.Equals(desiredVaultPath, _failedVaultPath, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		await ActivateAsync(desiredVaultPath, stoppingToken);
	}

	/// <summary>
	/// Resolves the vault path that user settings currently designate as active.
	/// </summary>
	private string? ResolveDesiredVaultPath()
	{
		var configuredPath = Environment.GetEnvironmentVariable("PLAINTORCH_VAULT_PATH")
			?? configurationStore.Load().ActiveVaultPath;

		if (string.IsNullOrWhiteSpace(configuredPath))
		{
			_failedVaultPath = null;
			return null;
		}

		var fullPath = Path.GetFullPath(configuredPath);
		if (!activationService.IsInitialized(fullPath))
		{
			if (!string.Equals(fullPath, _failedVaultPath, StringComparison.OrdinalIgnoreCase))
			{
				logger.LogWarning("Configured active vault '{VaultPath}' is not initialized. PLAINTORCH core will remain idle.", fullPath);
				hostState.Report(PlaintorchHostPhase.Failed, "The configured vault is not initialized for PLAINTORCH.", fullPath);
			}

			_failedVaultPath = fullPath;
			return null;
		}

		return fullPath;
	}

	/// <summary>
	/// Binds, locks, and initializes the specified vault, then marks the session ready to serve.
	/// </summary>
	private async Task ActivateAsync(string vaultPath, CancellationToken stoppingToken)
	{
		logger.LogInformation("Activating vault '{VaultPath}'.", vaultPath);
		layout.Bind(new VaultOptions { VaultPath = vaultPath });

		try
		{
			hostState.Report(PlaintorchHostPhase.Activating, "Acquiring vault lock...", vaultPath);
			_lockHandle = await lockService.AcquireAsync(stoppingToken);

			using (var scope = scopeFactory.CreateScope())
			{
				var engine = scope.ServiceProvider.GetRequiredService<PlaintorchEngine>();
				hostState.Report(PlaintorchHostPhase.Activating, "Initializing vault layout, database, and indexes...", vaultPath);
				await engine.InitializeVaultAsync(stoppingToken);
			}

			// Hand off to Active with the sweep flag already set, before MarkReady lets the watcher start, so a status
			// surface (the desktop splash) keeps waiting through the startup sweep with no window where it sees a
			// bare Active and closes early. The watcher clears the flag when its startup sweep ends (EndSweep); the
			// backstop below clears it if the watcher never gets there, so the flag can never wedge a splash open.
			hostState.Report(PlaintorchHostPhase.Active, "Vault ready. Running startup sweep...", vaultPath, sweeping: true);
			session.MarkReady(vaultPath);
			_activeVaultPath = vaultPath;
			_failedVaultPath = null;
			ScheduleSweepBackstop(vaultPath);

			logger.LogInformation("PLAINTORCH core is now serving vault '{VaultPath}'.", vaultPath);
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			await ReleaseVaultAsync();
			throw;
		}
		catch (Exception exception)
		{
			await ReleaseVaultAsync();
			_failedVaultPath = vaultPath;
			hostState.Report(PlaintorchHostPhase.Failed, $"Failed to activate vault '{vaultPath}'.{Environment.NewLine}{exception.Message}", vaultPath);
			logger.LogError(exception, "Failed to activate vault '{VaultPath}'. PLAINTORCH core will remain idle until the vault is reconfigured.", vaultPath);
		}
	}

	/// <summary>
	/// Stops serving the current vault and returns the core to idle.
	/// </summary>
	private async Task DeactivateAsync()
	{
		if (_activeVaultPath is null && !session.IsActive && _lockHandle is null)
		{
			return;
		}

		var releasedVaultPath = _activeVaultPath;
		await ReleaseVaultAsync();

		if (releasedVaultPath is not null)
		{
			logger.LogInformation("PLAINTORCH core released vault '{VaultPath}' and returned to idle.", releasedVaultPath);
			hostState.Report(PlaintorchHostPhase.Idle, "Vault released. Waiting for a vault to be activated.");
		}
	}

	/// <summary>
	/// Clears the startup-sweep flag after a bounded wait if the watcher never did, so a status surface can never be
	/// held on the sweep indefinitely (a watcher that faulted or was disabled). The normal path is the watcher's own
	/// <see cref="PlaintorchHostState.EndSweep"/> the moment its startup sweep finishes, long before this fires.
	/// </summary>
	private void ScheduleSweepBackstop(string vaultPath)
	{
		_ = Task.Delay(SweepBackstopTimeout).ContinueWith(_ =>
		{
			// Only clear if this same activation is still the one serving; a later deactivate/activate owns its own sweep.
			if (string.Equals(_activeVaultPath, vaultPath, StringComparison.OrdinalIgnoreCase) && hostState.Current.Sweeping)
			{
				hostState.EndSweep("Serving vault.");
			}
		}, TaskScheduler.Default);
	}

	/// <summary>
	/// Returns the core to idle: signals the session idle so vault-scoped work stops, releases the vault lock,
	/// unbinds the vault layout, and purges cached vault-scoped state.
	/// </summary>
	private async Task ReleaseVaultAsync()
	{
		hostState.EndSweep();
		session.MarkIdle();
		_activeVaultPath = null;

		if (_lockHandle is not null)
		{
			await _lockHandle.DisposeAsync();
			_lockHandle = null;
		}

		puckRuntimeCompilationCatalog.Purge();
		timeframeCandidateCache.Purge();
		layout.Unbind();
	}

	private FileSystemWatcher? CreateConfigurationWatcher()
	{
		try
		{
			var watcher = new FileSystemWatcher(userLayout.RootPath, Path.GetFileName(userLayout.ConfigurationPath))
			{
				NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
				EnableRaisingEvents = true,
			};

			watcher.Created += (_, _) => SignalReconcile();
			watcher.Changed += (_, _) => SignalReconcile();
			watcher.Deleted += (_, _) => SignalReconcile();
			watcher.Renamed += (_, _) => SignalReconcile();
			return watcher;
		}
		catch (Exception exception)
		{
			logger.LogWarning(exception, "Failed to watch user settings for vault activation changes. Falling back to periodic polling.");
			return null;
		}
	}

	private void SignalReconcile()
	{
		try
		{
			_reconcileSignal.Release();
		}
		catch (SemaphoreFullException)
		{
		}
	}

	/// <summary>
	/// Waits until the configuration watcher signals a change or the periodic safety interval elapses.
	/// </summary>
	private Task WaitForNextReconcileAsync(CancellationToken stoppingToken)
	{
		return _reconcileSignal.WaitAsync(ReconcileInterval, stoppingToken);
	}
}
