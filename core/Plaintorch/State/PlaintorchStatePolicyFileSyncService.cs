using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Watcher;

namespace Pleiades.Plaintorch.State;

/// <summary>
/// Applies non-database side effects produced by centralized PLAINTORCH state policies.
/// </summary>
public sealed class PlaintorchStatePolicyFileSyncService(
	MarkdownFileLocator markdownFileLocator,
	VaultWatcherWriteBarrier writeBarrier,
	IServiceScopeFactory scopeFactory)
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

	/// <summary>
	/// Reconciles the notes of directives a save bumped from Over back to Fulfilled (PEP102 patch 9). The status is a
	/// frontmatter field, so the note must follow the row: the save recorded a reconcile intent for each, and this
	/// drains it now rather than leaving it to the next pending-intent sweep. The drain runs on a scope of its own —
	/// the save that recorded the intent has committed, and its context is still inside its own save call, so it
	/// cannot be the one to clear the row.
	/// </summary>
	public async Task ReconcileReopenedDirectivesAsync(IEnumerable<StellarDirective> directives, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(directives);
		using var scope = scopeFactory.CreateScope();
		var writeQueue = scope.ServiceProvider.GetRequiredService<VaultWriteQueue>();
		foreach (var directive in directives)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await writeQueue.DrainReconcileAsync(directive, cancellationToken: cancellationToken);
		}
	}
}
