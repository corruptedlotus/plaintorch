using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Puck;
using Pleiades.Vault.Database;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Watcher;

namespace Pleiades.Plaintorch.Enforcement;

/// <summary>
/// Does the note work a save left once it commits (PEP103, <see cref="StateNoteWork"/>): what the state engine changed
/// in the database reaches the vault's files only when the change is durable.
/// </summary>
public sealed class StateNoteSyncService(
	MarkdownFileLocator markdownFileLocator,
	VaultWatcherWriteBarrier writeBarrier,
	IServiceScopeFactory scopeFactory)
{
	/// <summary>
	/// Deletes the notes of the forecasts the save superseded and rewrites the notes of the items the engine moved.
	/// </summary>
	public async Task ApplyAsync(StateNoteWork work, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(work);
		DeleteSupersededForecasts(work.SupersededForecasts, cancellationToken);
		if (work.Reconciles.Count > 0)
		{
			await ReconcileAsync(work.Reconciles, cancellationToken);
		}
	}

	private void DeleteSupersededForecasts(IEnumerable<PolarisCycle> forecasts, CancellationToken cancellationToken)
	{
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
	}

	/// <summary>
	/// Drains the reconcile intents the save recorded for the items the engine moved. The status is a frontmatter field, so
	/// each note must follow its row. The drain runs on a scope of its own: the save that recorded the intents has committed,
	/// and its context is still inside its own save call, so it cannot be the one to clear the rows. Each item is read anew
	/// on that scope, so its note is written from the row as committed, with everything its note shows loaded.
	/// </summary>
	private async Task ReconcileAsync(IEnumerable<object> entities, CancellationToken cancellationToken)
	{
		using var scope = scopeFactory.CreateScope();
		var gateway = scope.ServiceProvider.GetRequiredService<VaultEntityGateway>();
		var writeQueue = scope.ServiceProvider.GetRequiredService<VaultWriteQueue>();
		foreach (var entity in entities)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var committed = entity is IPuckNamedEntity named
				? await gateway.FindByIdAsync(entity.GetType(), named.Id, cancellationToken: cancellationToken)
				: null;
			await writeQueue.DrainReconcileAsync(committed ?? entity, cancellationToken: cancellationToken);
		}
	}
}
