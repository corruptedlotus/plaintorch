using Pleiades.Orchestration;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Watcher;

namespace Pleiades.Plaintorch.State;

/// <summary>
/// Applies non-database side effects produced by centralized PLAINTORCH state policies.
/// </summary>
public sealed class PlaintorchStatePolicyFileSyncService(
	MarkdownFileLocator markdownFileLocator,
	VaultWatcherWriteBarrier writeBarrier)
{
	/// <summary>
	/// Deletes markdown files for forecasts removed by centralized policy processing.
	/// </summary>
	public Task DeleteSupersededForecastsAsync(IEnumerable<PolarisCycle> forecasts, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(forecasts);

		foreach (var forecast in forecasts)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var path = markdownFileLocator.GetPolarisCycleFilePath(forecast);
			writeBarrier.Suppress(path);
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}

		return Task.CompletedTask;
	}
}