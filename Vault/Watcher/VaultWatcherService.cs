using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Watches vault-backed roots, performs a startup discovery scan, and debounces subsequent file events into a serialized reconciliation queue.
/// </summary>
public sealed class VaultWatcherService(
	IServiceScopeFactory scopeFactory,
	VaultPathSyncModelCatalog pathSyncModelCatalog,
	VaultWatcherWriteBarrier writeBarrier,
	ILogger<VaultWatcherService> logger) : BackgroundService
{
	private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(500);
	private readonly ConcurrentDictionary<string, DateTimeOffset> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<FileSystemWatcher> _watchers = [];

	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		await RunStartupScanAsync(stoppingToken);
		InitializeWatchers();

		using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
		try
		{
			while (await timer.WaitForNextTickAsync(stoppingToken))
			{
				await DrainPendingAsync(stoppingToken);
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
	}

	private async Task RunStartupScanAsync(CancellationToken cancellationToken)
	{
		using var scope = scopeFactory.CreateScope();
		var discovery = scope.ServiceProvider.GetRequiredService<VaultMarkdownDiscoveryService>();
		var result = await discovery.ScanAsync("startup", cancellationToken);
		logger.LogInformation(
			"Vault discovery completed: {CandidateCount} candidates, {InvalidCount} invalid, {IgnoredCount} ignored.",
			result.Candidates.Count,
			result.InvalidCount,
			result.IgnoredPaths);
		foreach (var candidate in result.Candidates.Where(candidate => !candidate.IsValid))
		{
			logger.LogWarning(
				"Invalid watcher candidate {Path} for {EntityType}: {IssueCount} issue(s). Suggested action: {Action}.",
				candidate.VaultRelativePath,
				candidate.Model.EntityName,
				candidate.Issues.Count,
				candidate.SuggestedAction);
		}
	}

	private void InitializeWatchers()
	{
		foreach (var root in pathSyncModelCatalog.GetModels().SelectMany(model => model.ScanRoots).Distinct(StringComparer.OrdinalIgnoreCase))
		{
			if (!Directory.Exists(root))
			{
				continue;
			}

			var watcher = new FileSystemWatcher(root)
			{
				IncludeSubdirectories = true,
				Filter = "*.md",
				NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
				EnableRaisingEvents = true,
			};

			watcher.Created += (_, eventArgs) => QueuePath(eventArgs.FullPath);
			watcher.Changed += (_, eventArgs) => QueuePath(eventArgs.FullPath);
			watcher.Deleted += (_, eventArgs) => QueuePath(eventArgs.FullPath);
			watcher.Renamed += (_, eventArgs) =>
			{
				QueuePath(eventArgs.OldFullPath);
				QueuePath(eventArgs.FullPath);
			};
			watcher.Error += (_, eventArgs) => logger.LogWarning(eventArgs.GetException(), "Vault watcher encountered a filesystem watcher error for '{Root}'.", root);

			_watchers.Add(watcher);
		}

		logger.LogInformation("Vault watcher initialized for {RootCount} root(s).", _watchers.Count);
	}

	private void QueuePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		if (writeBarrier.IsSuppressed(path))
		{
			return;
		}

		_pendingPaths[Path.GetFullPath(path)] = DateTimeOffset.UtcNow.Add(DebounceWindow);
	}

	private async Task DrainPendingAsync(CancellationToken cancellationToken)
	{
		var now = DateTimeOffset.UtcNow;
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

			await InspectPathAsync(path, cancellationToken);
		}
	}

	private async Task InspectPathAsync(string path, CancellationToken cancellationToken)
	{
		using var scope = scopeFactory.CreateScope();
		var discovery = scope.ServiceProvider.GetRequiredService<VaultMarkdownDiscoveryService>();
		var candidate = await discovery.InspectPathAsync(path, "watcher", cancellationToken);
		if (candidate is null)
		{
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
	}

	private void DisposeWatchers()
	{
		foreach (var watcher in _watchers)
		{
			watcher.Dispose();
		}

		_watchers.Clear();
	}
}
