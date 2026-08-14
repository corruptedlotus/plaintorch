using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pleiades.Diagnostics;
using Pleiades.Orchestration;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Policy;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Watches vault-backed roots, performs a startup discovery scan, and debounces subsequent file events into a serialized reconciliation queue.
/// </summary>
public sealed class VaultWatcherService(
	IServiceScopeFactory scopeFactory,
	VaultPathSyncModelCatalog pathSyncModelCatalog,
	VaultWatcherPathPolicy pathPolicy,
	VaultLayout layout,
	VaultWatcherWriteBarrier writeBarrier,
	WatcherStatusReporter statusReporter,
	OperationStatusRegistry statusRegistry,
	ILogger<VaultWatcherService> logger) : BackgroundService
{
	private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(500);
	private readonly ConcurrentDictionary<string, DateTimeOffset> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, (string NewPath, DateTimeOffset DueAt)> _pendingRelocations = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<FileSystemWatcher> _watchers = [];

	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		statusRegistry.SetHealthOverride(null);

		try
		{
			await RunStartupScanAsync(stoppingToken);
			statusReporter.ReportStartupScan(succeeded: true);
		}
		catch (Exception exception)
		{
			statusReporter.ReportStartupScan(succeeded: false, exception.Message);
			logger.LogError(exception, "Vault startup discovery scan failed. Watcher will continue with live filesystem observation.");
		}

		InitializeWatchers();

		using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
		try
		{
			while (await timer.WaitForNextTickAsync(stoppingToken))
			{
				try
				{
					await DrainPendingAsync(stoppingToken);
					statusReporter.ReportDrainTick(succeeded: true);
				}
				catch (Exception exception)
				{
					statusReporter.ReportDrainTick(succeeded: false, exception.Message);
					logger.LogError(exception, "Vault watcher tick failed while draining pending paths. Processing will continue.");
				}

				writeBarrier.PruneExpired();
			}
		}
		catch (OperationCanceledException)
		{
			statusRegistry.SetHealthOverride(OperationHealth.Suspended);
			logger.LogInformation("Vault watcher stopping.");
		}
		catch (Exception exception)
		{
			statusRegistry.SetHealthOverride(OperationHealth.Offline);
			statusReporter.ReportFatal(exception.Message);
			logger.LogError(exception, "Vault watcher crashed due to a fatal unhandled exception.");
			throw;
		}
		finally
		{
			DisposeWatchers();
		}
	}

	/// <summary>
	/// Runs startup scan reconciliation once before live filesystem observation begins.
	/// </summary>
	/// <param name="cancellationToken">A token used to cancel startup processing.</param>
	private async Task RunStartupScanAsync(CancellationToken cancellationToken)
	{
		using var scope = scopeFactory.CreateScope();
		var discovery = scope.ServiceProvider.GetRequiredService<VaultMarkdownDiscoveryService>();
		var syncService = scope.ServiceProvider.GetRequiredService<VaultWatcherSyncService>();
		var result = await discovery.ScanAsync("startup", cancellationToken);
		logger.LogInformation(
			"Vault discovery completed: {CandidateCount} candidates, {InvalidCount} invalid, {IgnoredCount} ignored.",
			result.Candidates.Count,
			result.InvalidCount,
			result.IgnoredPaths);
		var orderedCandidates = result.Candidates
			.OrderBy(candidate => StartupActionPriority(candidate.SuggestedAction))
			.ThenBy(candidate => candidate.VaultRelativePath, StringComparer.OrdinalIgnoreCase)
			.ToList();

		foreach (var candidate in orderedCandidates)
		{
			logger.LogInformation(
				"Startup discovery candidate {Path} for {EntityType} suggested action {Action}.",
				candidate.VaultRelativePath,
				candidate.Model.EntityName,
				candidate.SuggestedAction);

			if (!candidate.IsValid)
			{
				logger.LogWarning(
					"Startup candidate {Path} for {EntityType} has {IssueCount} issue(s). Suggested action: {Action}.",
					candidate.VaultRelativePath,
					candidate.Model.EntityName,
					candidate.Issues.Count,
					candidate.SuggestedAction);
			}

			try
			{
				await syncService.ExecuteAsync(candidate, "startup", cancellationToken);
			}
			catch (Exception exception)
			{
				logger.LogError(
					exception,
					"Watcher failed to process startup candidate '{Path}' for {EntityType}. Processing will continue.",
					candidate.VaultRelativePath,
					candidate.Model.EntityName);
			}
		}
	}

	/// <summary>
	/// Initializes filesystem watchers for all active scan roots.
	/// </summary>
	private void InitializeWatchers()
	{
		foreach (var root in pathPolicy.GetWatchRoots(pathSyncModelCatalog))
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
				watcher.Error += (_, eventArgs) =>
				{
					statusReporter.ReportRootError(root, eventArgs.GetException()?.Message);
					logger.LogWarning(eventArgs.GetException(), "Vault watcher encountered a filesystem watcher error for '{Root}'.", root);
				};

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
	}

	/// <summary>
	/// Queues a path for debounced inspection when it resolves to a supported markdown candidate.
	/// </summary>
	/// <param name="path">The raw watcher event path.</param>
	private void QueuePath(string path)
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
			if (!_pendingRelocations.TryRemove(relocation.OldPath, out _))
			{
				continue;
			}

			var handled = await TryProcessRelocationAsync(relocation.OldPath, relocation.NewPath, cancellationToken);
			if (handled)
			{
				_pendingPaths.TryRemove(relocation.OldPath, out _);
				_pendingPaths.TryRemove(relocation.NewPath, out _);
			}
		}

		var duePaths = _pendingPaths
			.Where(pair => pair.Value <= now)
			.Select(pair => pair.Key)
			.ToList();

		foreach (var path in duePaths)
		{
			if (!_pendingPaths.TryRemove(path, out _))
			{
				continue;
			}

			try
			{
				await InspectPathAsync(path, cancellationToken);
			}
			catch (Exception exception)
			{
				logger.LogError(exception, "Watcher failed to inspect path '{Path}'. Processing will continue.", path);
			}
		}
	}

	private void QueueRelocation(string oldPath, string newPath)
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
		var discovery = scope.ServiceProvider.GetRequiredService<VaultMarkdownDiscoveryService>();
		var syncService = scope.ServiceProvider.GetRequiredService<VaultWatcherSyncService>();
		VaultSyncCandidate? candidate;
		try
		{
			candidate = await discovery.InspectPathAsync(path, "watcher", cancellationToken);
		}
		catch (Exception exception)
		{
			statusReporter.ReportInspectFailure(path, exception);
			logger.LogError(exception, "Watcher discovery failed for path '{Path}'. Processing will continue.", path);
			return;
		}

		if (candidate is null)
		{
			statusReporter.ReportInspectIgnored(path);
			logger.LogDebug("Watcher ignored path '{Path}'.", path);
			return;
		}

		logger.LogInformation(
			"Watcher observed {EntityType} candidate '{Path}' with suggested action {Action}.",
			candidate.Model.EntityName,
			candidate.VaultRelativePath,
			candidate.SuggestedAction);
		if (!candidate.IsValid)
		{
			logger.LogWarning(
				"Watcher candidate '{Path}' has {IssueCount} validation issue(s).",
				candidate.VaultRelativePath,
				candidate.Issues.Count);
		}

		statusReporter.ReportInspectCandidate(candidate);

		try
		{
			await syncService.ExecuteAsync(candidate, "watcher", cancellationToken);
			statusReporter.ReportSyncSucceeded(candidate);
		}
		catch (Exception exception)
		{
			statusReporter.ReportSyncFailure(candidate, exception);
			logger.LogError(
				exception,
				"Watcher failed to process candidate '{Path}' for {EntityType}. Processing will continue.",
				candidate.VaultRelativePath,
				candidate.Model.EntityName);
		}
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

	private static int StartupActionPriority(VaultSyncAction action)
	{
		return action switch
		{
			VaultSyncAction.UpdateFromFile => 0,
			VaultSyncAction.RewriteFromDatabase => 0,
			VaultSyncAction.CreateFromFile => 1,
			VaultSyncAction.PurgeFile => 2,
			VaultSyncAction.Conflict => 3,
			VaultSyncAction.Ignore => 4,
			_ => 5,
		};
	}
}
