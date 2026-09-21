using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Markdown;

/// <summary>
/// The vault write queue (PEP110 Refactor BETA). The core records a durable <see cref="VaultWriteIntent"/> in the same
/// transaction as an entity change, and this drains it — reconciling the entity's markdown file to its database state
/// through <see cref="PlaintorchMarkdownStorageService"/> and then clearing the row. Draining synchronously in-request
/// keeps the file present when the API returns (so open-after-mutate flows still work); the startup
/// <see cref="DrainPendingAsync"/> recovers any intent a crash left between the database commit and the file write.
/// </summary>
/// <remarks>
/// Part 1b routes the directive write path through the queue for <see cref="VaultWriteIntentKind.Reconcile"/>. Removes,
/// the remaining entity types, and the bounded (2s) drain with the <c>noteReady</c> signal follow in later parts.
/// </remarks>
public sealed class VaultWriteQueue(
	PlainfraContext context,
	PlaintorchMarkdownStorageService storage,
	VaultEntityGateway entityGateway)
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
	/// Drains an entity's recorded reconcile intent now: writes its markdown file to match its database state, then
	/// clears the row. Runs in the request scope so the file is present when the call returns. If the write throws, the
	/// row is left behind for the startup drain / retry.
	/// </summary>
	public async Task DrainReconcileAsync(object entity, object? previous = null, CancellationToken cancellationToken = default)
	{
		await storage.SaveEntityAsync(entity, previous, cancellationToken: cancellationToken);
		var (entityType, id) = Identify(entity);
		await ClearAsync(entityType, id, cancellationToken);
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
