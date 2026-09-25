using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pleiades.Diagnostics;
using Pleiades.Orchestration;
using Pleiades.Resources;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Policy;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Watches vault-backed roots, performs a startup discovery scan, and debounces subsequent file events into a serialized reconciliation queue.
/// </summary>
/// <remarks>
/// The watcher is built so that <em>nothing it encounters can take the core down</em>. What it meets is graded on the
/// PEP108 severity scale (see <see cref="WatcherOperations"/>): a problem with one file — a locked or forbidden file,
/// invalid content, a sync that failed — is flagged and, when it is about reading or applying the file, retried
/// indefinitely; a problem that stops the watcher's whole job is <see cref="OperationSeverity.Fatal"/> and puts it to
/// sleep, on standby, until the condition clears. It sleeps when the vault is structurally unreachable (re-probing
/// access) and when a live span cannot start — the startup sweep failed or the watch roots could not be resolved —
/// (retrying the span on a backoff). Retry is not tracked separately — the live operation-status set <em>is</em> the
/// retry queue (see <see cref="WatcherRetryScheduler"/>), so what is being retried is exactly what the user sees flagged.
/// </remarks>
public sealed class VaultWatcherService(
	IServiceScopeFactory scopeFactory,
	VaultPathSyncModelCatalog pathSyncModelCatalog,
	VaultWatcherPathPolicy pathPolicy,
	VaultLayout layout,
	VaultWatcherWriteBarrier writeBarrier,
	WatcherStatusReporter statusReporter,
	OperationStatusRegistry statusRegistry,
	WatcherRetryScheduler retryScheduler,
	Pleiades.Plaintorch.ActiveVaultSession session,
	Pleiades.Plaintorch.Hosting.PlaintorchHostState hostState,
	ILogger<VaultWatcherService> logger) : BackgroundService
{
	private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(500);
	private static readonly TimeSpan DrainInterval = TimeSpan.FromMilliseconds(250);

	// How often the drain loop asks the status set which flagged scopes are due to be re-checked. The per-scope backoff
	// lives in WatcherRetryScheduler; this is only the cadence at which the question is asked.
	private static readonly TimeSpan RetrySweepInterval = TimeSpan.FromSeconds(2);

	// While asleep (tier 2), the watcher re-probes structural vault access on this interval and wakes itself when it
	// recovers — no re-activation required.
	private static readonly TimeSpan StructuralReprobeInterval = TimeSpan.FromSeconds(5);

	// While asleep because a live span could not start (the sweep failed, the roots would not resolve), the watcher
	// retries the span after a backoff that widens from the first to the last of these and stays there.
	private static readonly TimeSpan DegradedRetryInitial = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan DegradedRetryMax = TimeSpan.FromSeconds(60);

	private readonly ConcurrentDictionary<string, DateTimeOffset> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, (string NewPath, DateTimeOffset DueAt)> _pendingRelocations = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<FileSystemWatcher> _watchers = [];

	// Roots whose filesystem observer reported an error (events may have been lost), awaiting a re-sweep of the vault.
	private readonly ConcurrentDictionary<string, byte> _resweepRoots = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>How a live-observation session ended, so the supervisor knows whether to sleep or exit.</summary>
	private enum LiveSessionExit
	{
		/// <summary>The session token was cancelled (deactivation or host shutdown).</summary>
		Cancelled,

		/// <summary>Structural vault access was lost; the supervisor should sleep and re-probe.</summary>
		LostAccess,

		/// <summary>
		/// The span could not start (the startup sweep failed, or the watch roots could not be resolved); the supervisor
		/// should sleep on a backoff and retry the span.
		/// </summary>
		Degraded,
	}

	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var servedGeneration = 0L;
		while (!stoppingToken.IsCancellationRequested)
		{
			long generation;
			CancellationToken sessionToken;
			try
			{
				(generation, sessionToken) = await session.WaitForSessionAsync(servedGeneration, stoppingToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			// Record the generation before running so a session is never re-entered, even if RunSessionAsync
			// returns early. The next iteration then blocks until a newer activation, preventing a hot loop.
			servedGeneration = generation;
			statusRegistry.SetHealthOverride(OperationHealth.Standby);

			using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, sessionToken);
			try
			{
				await RunSessionAsync(linkedCts.Token);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
			catch (OperationCanceledException)
			{
				// The active vault was deactivated. Fall through to await the next activation.
			}
			catch (Exception exception)
			{
				// Final backstop: the watcher must never fault its way out of ExecuteAsync, because a faulted
				// BackgroundService stops the host. A session that fell through every inner guard is flagged as fatal and
				// logged, and the watcher waits for the next activation rather than taking the core down with it.
				statusReporter.ReportFatal(exception.Message);
				logger.LogCritical(exception, "Vault watcher session terminated unexpectedly. The watcher will resume when a vault is next activated.");
			}

			// No session is being served: the watcher is on standby until the next activation.
			statusRegistry.SetHealthOverride(OperationHealth.Standby);
		}
	}

	/// <summary>
	/// Runs one vault-serving watcher session. The session alternates between sleeping while the vault is structurally
	/// unreachable and live observation while it is reachable; a structural failure never ends the session or the host —
	/// the watcher sleeps, re-probes, and resumes itself when access returns.
	/// </summary>
	/// <param name="stoppingToken">A token scoped to the active vault session.</param>
	private async Task RunSessionAsync(CancellationToken stoppingToken)
	{
		// A session starts from a clean status set: nothing flagged for a previous vault (or a previous run of this one)
		// may linger — a fatal status left over would hold the new session on standby. Then the standby override is
		// cleared so health reflects the derived rollup again (PEP108).
		statusRegistry.Reset();
		statusRegistry.SetHealthOverride(null);

		// Load this vault's durable status dismissals (PEP108 dismiss feature) before the scan re-raises its statuses,
		// so a snoozed issue stays snoozed across restarts. Matching is by value, so a re-raised status is dismissed.
		await LoadDismissalsAsync(stoppingToken);

		try
		{
			var degradedAttempts = 0;
			while (!stoppingToken.IsCancellationRequested)
			{
				if (!await WaitForStructuralAccessAsync(stoppingToken))
				{
					return; // cancelled while asleep
				}

				switch (await RunLiveSessionAsync(stoppingToken))
				{
					case LiveSessionExit.Cancelled:
						return;

					case LiveSessionExit.Degraded:
						// The span could not start; sleep on a widening backoff and try it again from the sweep.
						if (!await SleepDegradedAsync(++degradedAttempts, stoppingToken))
						{
							return;
						}

						break;

					default:
						// LostAccess: loop back to sleep and re-probe until the vault is reachable again.
						degradedAttempts = 0;
						break;
				}
			}
		}
		finally
		{
			// The status and dismissal sets are vault-scoped; drop them when the session ends so a different vault does
			// not inherit them.
			statusRegistry.Reset();
			statusRegistry.ClearDismissals();
		}
	}

	/// <summary>
	/// Ensures the vault is structurally reachable before live observation begins. When it is not (tier 2), the watcher
	/// raises an error-level issue, sleeps, and re-probes on an interval until access is restored — clearing the issue
	/// and returning so the caller resumes with a full sweep. Returns <see langword="false"/> only when cancelled.
	/// </summary>
	private async Task<bool> WaitForStructuralAccessAsync(CancellationToken stoppingToken)
	{
		if (pathPolicy.IsVaultStructurallyAccessible(out var inaccessiblePath))
		{
			return true;
		}

		var scope = inaccessiblePath ?? WatcherOperations.GlobalScope;
		// Inaccessible before the first sweep even runs: the core is serving, so stop holding a splash on the sweep.
		hostState.EndSweep("Serving vault (watcher on standby, waiting for vault files).");
		statusReporter.ReportVaultInaccessible(scope, WatcherMessages.Details.VaultPathNotAccessible(scope));
		logger.LogError("Vault watcher is asleep: '{Path}' is not accessible. It will re-probe until access is restored.", scope);

		// Asleep is standby, whatever the status set says (PEP108).
		statusRegistry.SetHealthOverride(OperationHealth.Standby);
		using var timer = new PeriodicTimer(StructuralReprobeInterval);
		try
		{
			while (await timer.WaitForNextTickAsync(stoppingToken))
			{
				if (pathPolicy.IsVaultStructurallyAccessible(out _))
				{
					statusReporter.ReportVaultAccessible();
					statusRegistry.SetHealthOverride(null);
					logger.LogInformation("Vault watcher woke: access to '{Path}' restored. Resuming with a full sweep.", scope);
					return true;
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Cancelled while asleep; fall through.
		}

		return false;
	}

	/// <summary>
	/// Sleeps on standby after a live span could not start (a fatal status says why), for a backoff that widens with
	/// each consecutive attempt, then returns so the caller retries the span — whose sweep resolves the status once it
	/// succeeds. Returns <see langword="false"/> only when cancelled.
	/// </summary>
	private async Task<bool> SleepDegradedAsync(int attempt, CancellationToken stoppingToken)
	{
		var delay = TimeSpan.FromTicks(Math.Min(DegradedRetryMax.Ticks, DegradedRetryInitial.Ticks * (1L << Math.Min(attempt - 1, 16))));
		logger.LogError("Vault watcher is asleep: its live span could not start. It will retry in {Delay}.", delay);
		statusRegistry.SetHealthOverride(OperationHealth.Standby);
		try
		{
			await Task.Delay(delay, stoppingToken);
			return true;
		}
		catch (OperationCanceledException)
		{
			return false;
		}
		finally
		{
			statusRegistry.SetHealthOverride(null);
		}
	}

	/// <summary>
	/// Runs one span of live observation: a startup sweep, filesystem watchers, and the debounced drain/retry loop.
	/// Returns how the span ended so the supervisor can either sleep (structural access lost) or exit (cancelled).
	/// </summary>
	private async Task<LiveSessionExit> RunLiveSessionAsync(CancellationToken stoppingToken)
	{
		try
		{
			await RunStartupScanAsync(stoppingToken);
			statusReporter.ReportStartupScan(succeeded: true);
		}
		catch (OperationCanceledException)
		{
			return LiveSessionExit.Cancelled;
		}
		catch (Exception exception)
		{
			if (IsStructuralFailure(out var scope))
			{
				// The sweep will not complete this span; the core still serves, so release any splash held on the sweep.
				hostState.EndSweep("Serving vault (watcher sleeping until vault files are reachable).");
				statusReporter.ReportVaultInaccessible(scope, exception.Message);
				logger.LogWarning(exception, "Vault startup scan failed because '{Path}' is inaccessible; watcher will sleep and re-probe.", scope);
				return LiveSessionExit.LostAccess;
			}

			// Without its sweep the watcher cannot trust anything it would observe (a broken database fails every
			// reconcile alike): the failure is fatal, and the span ends so the supervisor sleeps and retries it.
			hostState.EndSweep("Serving vault (watcher on standby: the startup sweep failed).");
			statusReporter.ReportStartupScan(succeeded: false, exception.Message);
			logger.LogCritical(exception, "Vault startup discovery scan failed. The watcher is on standby and will retry it.");
			return LiveSessionExit.Degraded;
		}

		// The startup sweep is done; clear the sweep flag so a status surface stops waiting on it.
		hostState.EndSweep("Serving vault.");
		if (!InitializeWatchers())
		{
			DisposeWatchers();
			return LiveSessionExit.Degraded;
		}

		using var timer = new PeriodicTimer(DrainInterval);
		var lastMaintenance = DateTimeOffset.MinValue;
		try
		{
			while (await timer.WaitForNextTickAsync(stoppingToken))
			{
				try
				{
					await DrainPendingAsync(stoppingToken);

					// Maintenance runs on a coarser cadence than the drain: re-probe structural access (tier 2) and
					// re-queue due issue scopes (the retry sweep) here rather than every tick, so a healthy vault is
					// not stat-ed four times a second.
					var now = DateTimeOffset.UtcNow;
					if (now - lastMaintenance >= RetrySweepInterval)
					{
						lastMaintenance = now;

						if (!pathPolicy.IsVaultStructurallyAccessible(out var inaccessiblePath))
						{
							var scope = inaccessiblePath ?? WatcherOperations.GlobalScope;
							statusReporter.ReportVaultInaccessible(scope, WatcherMessages.Details.VaultPathBecameInaccessible(scope));
							logger.LogWarning("Vault watcher lost access to '{Path}' mid-session; going to sleep.", scope);
							return LiveSessionExit.LostAccess;
						}

						RequeueDueIssueScopes(now);
						await ResweepAfterObserverErrorsAsync(stoppingToken);
					}

					statusReporter.ReportDrainTick(succeeded: true);
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (Exception exception)
				{
					// A single tick failing is never fatal: report it and keep draining. The core must never go down
					// with a tick, so nothing here rethrows.
					statusReporter.ReportDrainTick(succeeded: false, exception.Message);
					logger.LogError(exception, "Vault watcher tick failed while draining pending paths. Processing will continue.");
				}

				writeBarrier.PruneExpired();
			}
		}
		catch (OperationCanceledException)
		{
			logger.LogInformation("Vault watcher stopping.");
		}
		finally
		{
			DisposeWatchers();
		}

		return LiveSessionExit.Cancelled;
	}

	/// <summary>
	/// Re-sweeps the vault when a filesystem observer reported an error since the last maintenance step — its events may
	/// have been lost, so the watcher's view is stale — and resolves those roots' flags once the sweep has run. A failing
	/// sweep puts the roots back for the next step and lets the tick report the failure.
	/// </summary>
	private async Task ResweepAfterObserverErrorsAsync(CancellationToken stoppingToken)
	{
		if (_resweepRoots.IsEmpty)
		{
			return;
		}

		var roots = _resweepRoots.Keys.ToList();
		foreach (var root in roots)
		{
			_resweepRoots.TryRemove(root, out _);
		}

		try
		{
			await RunSweepAsync("watcher-resweep", stoppingToken);
		}
		catch
		{
			foreach (var root in roots)
			{
				_resweepRoots.TryAdd(root, 0);
			}

			throw;
		}

		foreach (var root in roots)
		{
			statusReporter.ReportRootRecovered(root);
		}

		logger.LogInformation("Vault watcher re-swept the vault after an observer error on {RootCount} root(s).", roots.Count);
	}

	/// <summary>
	/// Classifies whether a failure that just occurred is a structural (whole-of-vault, tier 2) condition, by probing
	/// current access rather than by parsing the exception. When it returns <see langword="true"/>, <paramref name="scope"/>
	/// names the inaccessible root.
	/// </summary>
	private bool IsStructuralFailure(out string scope)
	{
		var accessible = pathPolicy.IsVaultStructurallyAccessible(out var inaccessiblePath);
		scope = inaccessiblePath ?? WatcherOperations.GlobalScope;
		return !accessible;
	}

	/// <summary>
	/// Re-queues the flagged scopes the retry scheduler reports as due, so a normal inspection pass re-checks them. A
	/// clean pass resolves the status through ordinary diff-based reporting; a still-failing one refreshes and widens
	/// its own backoff. This — not a separate attempt counter — is the whole retry mechanism.
	/// </summary>
	private void RequeueDueIssueScopes(DateTimeOffset now)
	{
		foreach (var path in retryScheduler.DuePaths(statusRegistry.GetActiveStatuses(), now))
		{
			if (string.IsNullOrWhiteSpace(path) || writeBarrier.IsSuppressed(path))
			{
				continue;
			}

			// Due immediately (no debounce): this is a deliberate retry, not a noisy filesystem event.
			_pendingPaths[Path.GetFullPath(path)] = now;
		}
	}

	/// <summary>Loads the active vault's durable status dismissals into the registry (PEP108 dismiss feature).</summary>
	private async Task LoadDismissalsAsync(CancellationToken cancellationToken)
	{
		try
		{
			using var scope = scopeFactory.CreateScope();
			var dismissals = scope.ServiceProvider.GetRequiredService<Pleiades.Plaintorch.Diagnostics.OperationStatusDismissalService>();
			await dismissals.LoadIntoRegistryAsync(cancellationToken);
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Failed to load durable status dismissals for the active vault. Dismissed issues may resurface until the next activation.");
		}
	}

	/// <summary>
	/// Runs startup scan reconciliation once before live filesystem observation begins, through the same reconciler the
	/// live path uses — so a sweep emits the same issues a sequence of live events would. A structural discovery failure
	/// propagates (for the caller to classify as tier 2); per-candidate failures are reported and skipped.
	/// </summary>
	/// <param name="cancellationToken">A token used to cancel startup processing.</param>
	private Task RunStartupScanAsync(CancellationToken cancellationToken)
		=> RunSweepAsync("startup", cancellationToken);

	/// <summary>Reconciles every candidate in the vault through the reconciler the live path uses, for an origin.</summary>
	private async Task RunSweepAsync(string origin, CancellationToken cancellationToken)
	{
		using var scope = scopeFactory.CreateScope();
		var reconciler = scope.ServiceProvider.GetRequiredService<VaultWatcherReconciler>();
		await reconciler.ReconcileSweepAsync(origin, cancellationToken);
	}

	/// <summary>
	/// Initializes filesystem watchers for all active scan roots. Returns <see langword="false"/> when the roots could
	/// not be resolved at all — a fatal condition (no live observation), which the caller answers by sleeping and
	/// retrying; a single root that fails to initialize is only critical, and the others are still observed.
	/// </summary>
	private bool InitializeWatchers()
	{
		IReadOnlyList<string> roots;
		try
		{
			roots = pathPolicy.GetWatchRoots(pathSyncModelCatalog);
		}
		catch (Exception exception)
		{
			// Failing to resolve watch roots must not escape: it is reported as fatal and the supervisor retries the span.
			statusReporter.ReportRootsResolved(succeeded: false, exception.Message);
			logger.LogCritical(exception, "Vault watcher failed to resolve watch roots. The watcher is on standby and will retry.");
			return false;
		}

		statusReporter.ReportRootsResolved(succeeded: true);

		foreach (var root in roots)
		{
			if (!Directory.Exists(root))
			{
				continue;
			}

			try
			{
				var watcher = new FileSystemWatcher(root)
				{
					IncludeSubdirectories = true,
					Filter = "*",
					NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
					EnableRaisingEvents = true,
				};

				watcher.Created += (_, eventArgs) => QueuePath(eventArgs.FullPath);
				watcher.Changed += (_, eventArgs) => QueuePath(eventArgs.FullPath);
				watcher.Deleted += (_, eventArgs) => QueuePath(eventArgs.FullPath);
				watcher.Renamed += (_, eventArgs) =>
				{
					QueueRelocation(eventArgs.OldFullPath, eventArgs.FullPath);
					QueuePath(eventArgs.OldFullPath);
					QueuePath(eventArgs.FullPath);
				};
				watcher.Error += (_, eventArgs) => HandleWatcherError(root, eventArgs.GetException());

				statusReporter.ReportRootInitialized(root);

				_watchers.Add(watcher);
			}
			catch (Exception exception)
			{
				statusReporter.ReportRootInitializationFailed(root, exception.Message);
				logger.LogError(exception, "Vault watcher failed to initialize filesystem watcher for '{Root}'. Processing will continue for remaining roots.", root);
			}
		}

		logger.LogInformation("Vault watcher initialized for {RootCount} root(s).", _watchers.Count);
		return true;
	}

	private void HandleWatcherError(string root, Exception? exception)
	{
		// Runs on a filesystem-watcher callback thread; it must never throw.
		try
		{
			statusReporter.ReportRootError(root, exception?.Message);
			_resweepRoots.TryAdd(root, 0);
			logger.LogError(exception, "Vault watcher encountered a filesystem watcher error for '{Root}'; it will re-sweep the vault.", root);
		}
		catch (Exception handlerException)
		{
			logger.LogError(handlerException, "Vault watcher error handler itself failed for '{Root}'.", root);
		}
	}

	/// <summary>
	/// Queues a path for debounced inspection when it resolves to a supported markdown candidate. Runs on a
	/// filesystem-watcher callback thread, so it must never let an exception escape — an unobserved throw here would
	/// crash the process.
	/// </summary>
	/// <param name="path">The raw watcher event path.</param>
	private void QueuePath(string path)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				return;
			}

			if (pathPolicy.ShouldIgnorePath(path))
			{
				return;
			}

			var fullPath = Path.GetFullPath(path);
			var isDirectoryEvent = Directory.Exists(fullPath);
			var isMarkdownPath = string.Equals(Path.GetExtension(fullPath), ".md", StringComparison.OrdinalIgnoreCase);
			if (!isDirectoryEvent && !isMarkdownPath)
			{
				return;
			}

			if (pathSyncModelCatalog.TryResolveWatchPath(path, out var inspectPath, out _)
				&& !string.IsNullOrWhiteSpace(inspectPath)
				&& !writeBarrier.IsSuppressed(inspectPath))
			{
				_pendingPaths[Path.GetFullPath(inspectPath)] = DateTimeOffset.UtcNow.Add(DebounceWindow);
				return;
			}

			if (writeBarrier.IsSuppressed(path))
			{
				return;
			}

			_pendingPaths[fullPath] = DateTimeOffset.UtcNow.Add(DebounceWindow);
		}
		catch (Exception exception)
		{
			// Swallow: a filesystem event we cannot even queue is not worth a process crash. If the path genuinely needs
			// reconciliation, a later event or the retry sweep will pick it up.
			logger.LogWarning(exception, "Vault watcher could not queue path '{Path}' from a filesystem event; ignoring it.", path);
		}
	}

	/// <summary>
	/// Drains pending debounced paths whose due time has elapsed.
	/// </summary>
	/// <param name="cancellationToken">A token used to cancel draining.</param>
	private async Task DrainPendingAsync(CancellationToken cancellationToken)
	{
		var now = DateTimeOffset.UtcNow;
		var dueRelocations = _pendingRelocations
			.Where(pair => pair.Value.DueAt <= now)
			.Select(pair => (OldPath: pair.Key, pair.Value.NewPath))
			.ToList();

		foreach (var relocation in dueRelocations)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!_pendingRelocations.TryRemove(relocation.OldPath, out _))
			{
				continue;
			}

			try
			{
				var handled = await TryProcessRelocationAsync(relocation.OldPath, relocation.NewPath, cancellationToken);
				if (handled)
				{
					_pendingPaths.TryRemove(relocation.OldPath, out _);
					_pendingPaths.TryRemove(relocation.NewPath, out _);
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception exception)
			{
				// The relocation fast-path is best-effort; if it throws, fall back to a plain inspection of the new
				// location on a later tick so the move is still reconciled rather than lost.
				_pendingPaths[Path.GetFullPath(relocation.NewPath)] = DateTimeOffset.UtcNow;
				statusReporter.ReportRelocationFailed(relocation.NewPath, exception.Message);
				logger.LogWarning(exception, "Watcher failed to process relocation to '{NewPath}'; re-queued it for plain inspection.", relocation.NewPath);
			}
		}

		var duePaths = _pendingPaths
			.Where(pair => pair.Value <= now)
			.Select(pair => pair.Key)
			.ToList();

		foreach (var path in duePaths)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!_pendingPaths.TryRemove(path, out _))
			{
				continue;
			}

			try
			{
				await InspectPathAsync(path, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception exception)
			{
				// InspectPathAsync reports and grades its own failures; this is a belt-and-suspenders guard so one path
				// can never abort the drain of the rest.
				logger.LogError(exception, "Watcher failed to inspect path '{Path}'. Processing will continue.", path);
			}
		}
	}

	private void QueueRelocation(string oldPath, string newPath)
	{
		// Runs on a filesystem-watcher callback thread; it must never let an exception escape.
		try
		{
			if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath))
			{
				return;
			}

			if (pathPolicy.ShouldIgnorePath(oldPath) || pathPolicy.ShouldIgnorePath(newPath))
			{
				return;
			}

			if (!TryResolveInspectablePath(oldPath, out var inspectOld)
				|| !TryResolveInspectablePath(newPath, out var inspectNew)
				|| string.IsNullOrWhiteSpace(inspectOld)
				|| string.IsNullOrWhiteSpace(inspectNew))
			{
				return;
			}

			_pendingRelocations[inspectOld] = (inspectNew, DateTimeOffset.UtcNow.Add(DebounceWindow));
		}
		catch (Exception exception)
		{
			logger.LogWarning(exception, "Vault watcher could not queue relocation '{OldPath}' -> '{NewPath}'; ignoring it.", oldPath, newPath);
		}
	}

	private bool TryResolveInspectablePath(string path, out string? inspectPath)
	{
		if (pathPolicy.ShouldIgnorePath(path))
		{
			inspectPath = null;
			return false;
		}

		if (pathSyncModelCatalog.TryResolveWatchPath(path, out inspectPath, out _)
			&& !string.IsNullOrWhiteSpace(inspectPath))
		{
			inspectPath = Path.GetFullPath(inspectPath);
			return true;
		}

		inspectPath = null;
		return false;
	}

	private async Task<bool> TryProcessRelocationAsync(string oldPath, string newPath, CancellationToken cancellationToken)
	{
		if (pathPolicy.ShouldIgnorePath(oldPath) || pathPolicy.ShouldIgnorePath(newPath))
		{
			return false;
		}

		if (writeBarrier.IsSuppressed(oldPath) || writeBarrier.IsSuppressed(newPath))
		{
			return false;
		}

		using var scope = scopeFactory.CreateScope();
		var discovery = scope.ServiceProvider.GetRequiredService<VaultMarkdownDiscoveryService>();
		var syncService = scope.ServiceProvider.GetRequiredService<VaultWatcherSyncService>();
		var policyRouter = scope.ServiceProvider.GetRequiredService<VaultStorageModePolicyRouter>();

		var candidate = await discovery.InspectPathAsync(newPath, "watcher-relocation", cancellationToken);
		if (candidate is null || string.IsNullOrWhiteSpace(candidate.PathId))
		{
			return false;
		}

		var oldId = ResolvePathId(candidate.Model.EntityType, oldPath);
		var policy = policyRouter.Resolve(candidate.Model.Mode);
		oldId = policy.ResolveRelocationOldIdFallback(candidate.Model, oldId, candidate.PathId);

		if (string.IsNullOrWhiteSpace(oldId)
			|| !string.Equals(oldId, candidate.PathId, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		logger.LogInformation(
			"Watcher detected relocation from '{OldPath}' to '{NewPath}' for {EntityType} '{EntityId}' and will prioritize relocation sync.",
			oldPath,
			newPath,
			candidate.Model.EntityName,
			candidate.PathId);

		await syncService.ExecuteAsync(candidate, "watcher-relocation", cancellationToken);
		statusReporter.ReportRelocationSucceeded(newPath);
		return true;
	}

	private string? ResolvePathId(Type entityType, string path)
	{
		if (entityType == typeof(Saga.LorePage))
		{
			var lore = new Saga.LorePage
			{
				Id = string.Empty,
				Title = string.Empty,
			};

			return MarkdownFileLocator.ApplyLorePageCompositionFromPath(lore, path, layout.VaultRoot, layout.SagaRoot)
				? lore.Id
				: null;
		}

		var parsed = MarkdownFileLocator.ParseLoosePuckIdentityFromPath(path);
		return parsed.Id;
	}

	/// <summary>
	/// Inspects a single path and executes the resulting synchronization action.
	/// </summary>
	/// <param name="path">The path to inspect.</param>
	/// <param name="cancellationToken">A token used to cancel inspection.</param>
	private async Task InspectPathAsync(string path, CancellationToken cancellationToken)
	{
		if (pathPolicy.ShouldIgnorePath(path))
		{
			return;
		}

		using var scope = scopeFactory.CreateScope();
		var reconciler = scope.ServiceProvider.GetRequiredService<VaultWatcherReconciler>();
		await reconciler.ReconcilePathAsync(path, "watcher", cancellationToken);
	}

	/// <summary>
	/// Disposes all active filesystem watchers.
	/// </summary>
	private void DisposeWatchers()
	{
		foreach (var watcher in _watchers)
		{
			watcher.Dispose();
		}

		_watchers.Clear();
	}
}
