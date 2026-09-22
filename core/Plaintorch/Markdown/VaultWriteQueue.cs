using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Preferences;
using Pleiades.Puck;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Markdown;

/// <summary>
/// The vault write queue (PEP110 Refactor BETA). The core records a durable <see cref="VaultWriteIntent"/> in the same
/// transaction as an entity change, and this drains it — reconciling the entity's markdown file to its database state
/// through <see cref="PlaintorchMarkdownStorageService"/> and then clearing the row. The drain is bounded by the user's
/// <see cref="WatcherPreferences.NoteQueueTimeout"/>: within it the file is on disk when the API returns (so
/// open-after-mutate flows work) and the write is reported ready; past it the drain continues on its own scope and the
/// write is reported pending. The startup <see cref="DrainPendingAsync"/> recovers any intent a crash left between the
/// database commit and the file write.
/// </summary>
/// <remarks>
/// Part 1b routes the directive write path through the queue for <see cref="VaultWriteIntentKind.Reconcile"/>. Removes
/// and the remaining entity types follow in later parts; the <c>noteReady</c> flag returned here is surfaced to the
/// client (banners / create-then-open) in the same later work.
/// </remarks>
public sealed class VaultWriteQueue(
	PlainfraContext context,
	PlaintorchMarkdownStorageService storage,
	VaultEntityGateway entityGateway,
	IOptionsSnapshot<WatcherPreferences> watcherPreferences,
	ILogger<VaultWriteQueue> logger)
{
	/// <summary>
	/// Records a reconcile intent for an entity onto the current context, to be committed atomically with the entity
	/// change that follows. One row per entity: a repeat record idempotently refreshes the existing row.
	/// </summary>
	public async Task RecordReconcileAsync(object entity, CancellationToken cancellationToken = default)
	{
		var (entityType, id) = Identify(entity);
		var existing = await context.VaultWriteIntents.FindAsync([entityType, id], cancellationToken);
		if (existing is null)
		{
			context.VaultWriteIntents.Add(new VaultWriteIntent
			{
				EntityType = entityType,
				EntityId = id,
				Kind = VaultWriteIntentKind.Reconcile,
				Identity = id,
				EnqueuedUtc = DateTimeOffset.UtcNow,
			});
		}
		else
		{
			existing.Kind = VaultWriteIntentKind.Reconcile;
			existing.Identity = id;
			existing.EnqueuedUtc = DateTimeOffset.UtcNow;
		}
	}

	/// <summary>
	/// Drains an entity's recorded reconcile intent, bounded by <see cref="WatcherPreferences.NoteQueueTimeout"/>. The
	/// drain runs inline on the request's scope (one SQLite connection, no cross-scope contention); the timeout bounds
	/// only how long the caller <em>waits</em> for it. It returns <see langword="true"/> (the file is on disk) when it
	/// completes within the timeout, or <see langword="false"/> when the timeout elapses first — the drain then runs on
	/// best-effort to completion, and the durable row guarantees the write is finished (or, if the scope is torn down
	/// first, recovered by the startup drain). A non-positive timeout means no bound (wait for completion). If the drain
	/// throws, the row is left behind for the startup drain / retry.
	/// </summary>
	/// <returns><see langword="true"/> when the file was written within the timeout; otherwise <see langword="false"/> (pending).</returns>
	public async Task<bool> DrainReconcileAsync(object entity, object? previous = null, CancellationToken cancellationToken = default)
	{
		var (entityType, id) = Identify(entity);
		var drainTask = DrainInlineAsync(entity, previous, entityType, id, cancellationToken);
		var timeout = ResolveTimeout();
		if (timeout <= TimeSpan.Zero)
		{
			await drainTask;
			return true;
		}

		try
		{
			// Wait for the inline drain up to the timeout only; WaitAsync leaves the underlying drain running.
			await drainTask.WaitAsync(timeout, cancellationToken);
			return true;
		}
		catch (TimeoutException)
		{
			// Past the timeout: the write is reported pending; the drain finishes on best-effort and the durable row (or
			// the startup drain, if this scope is torn down first) guarantees it lands.
			ObserveInBackground(drainTask);
			return false;
		}
	}

	private async Task DrainInlineAsync(object entity, object? previous, string entityType, string id, CancellationToken cancellationToken)
	{
		await storage.SaveEntityAsync(entity, previous, cancellationToken: cancellationToken);
		await ClearAsync(entityType, id, cancellationToken);
	}

	private TimeSpan ResolveTimeout()
	{
		var milliseconds = watcherPreferences.Value.NoteQueueTimeout;
		return milliseconds <= 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(milliseconds);
	}

	private void ObserveInBackground(Task task)
	{
		_ = task.ContinueWith(
			completed => logger.LogWarning(completed.Exception, "A backgrounded vault write drain faulted; its intent will be retried by the startup drain."),
			CancellationToken.None,
			TaskContinuationOptions.OnlyOnFaulted,
			TaskScheduler.Default);
	}

	/// <summary>
	/// Reconciles every pending write intent — the startup drain that recovers intents a crash left between the database
	/// commit and the file write. Each entity is reloaded from the database and its file re-written; a reconcile whose
	/// entity has since vanished simply clears its row.
	/// </summary>
	public async Task DrainPendingAsync(CancellationToken cancellationToken = default)
	{
		// Order client-side: the outbox is a near-empty dirty-set, and SQLite cannot ORDER BY a DateTimeOffset column.
		var pending = (await context.VaultWriteIntents.ToListAsync(cancellationToken))
			.OrderBy(intent => intent.EnqueuedUtc)
			.ToList();

		foreach (var intent in pending)
		{
			if (intent.Kind == VaultWriteIntentKind.Reconcile)
			{
				var type = ResolveEntityType(intent.EntityType);
				var entity = type is null
					? null
					: await entityGateway.FindByIdAsync(type, intent.EntityId, cancellationToken: cancellationToken);
				if (entity is not null)
				{
					await storage.SaveEntityAsync(entity, cancellationToken: cancellationToken);
				}
			}

			context.VaultWriteIntents.Remove(intent);
		}

		if (pending.Count > 0)
		{
			await context.SaveChangesAsync(cancellationToken);
		}
	}

	private async Task ClearAsync(string entityType, string id, CancellationToken cancellationToken)
	{
		var existing = await context.VaultWriteIntents.FindAsync([entityType, id], cancellationToken);
		if (existing is not null)
		{
			context.VaultWriteIntents.Remove(existing);
			await context.SaveChangesAsync(cancellationToken);
		}
	}

	private static (string EntityType, string Id) Identify(object entity)
	{
		ArgumentNullException.ThrowIfNull(entity);
		if (entity is not IPuckNamedEntity named || string.IsNullOrWhiteSpace(named.Id))
		{
			throw new InvalidOperationException($"Entity '{entity.GetType().Name}' has no PUCK identity to queue a vault write for.");
		}

		return (entity.GetType().FullName!, named.Id);
	}

	// Every vault-backed entity lives in the core assembly (anchored here by Directive), so resolve the stored concrete
	// type name from it directly rather than relying on the ambient assembly-load context.
	private static Type? ResolveEntityType(string fullName)
		=> typeof(Directive).Assembly.GetType(fullName, throwOnError: false);
}
