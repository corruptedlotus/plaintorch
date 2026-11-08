using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pleiades.Puck;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Enforcement;

/// <summary>
/// The state interceptor (PEP103): the one place the core enforces the backlog's state. Every save of a
/// <see cref="PlainfraContext"/> runs the <see cref="StateEngine"/> before anything is written — asynchronous or
/// synchronous, from the API, the markdown watcher, an activation or a background pass — so no pathway applies a state rule
/// another skips, and every decision a rule calls for is taken through the backlog state enforcement level
/// (<see cref="StateDecisionService"/>). A save the engine stops throws <see cref="StateEnforcementException"/> and writes
/// nothing. The note work only a committed save may do — deleting superseded forecasts, rewriting the notes of the items the
/// engine moved — runs once the save commits (<see cref="StateNoteSyncService"/>).
/// </summary>
/// <remarks>
/// <para>
/// Registered first, so the interceptors after it (the change feed, the caches) see the save the engine leaves behind rather
/// than what the caller asked for.
/// </para>
/// <para>
/// The synchronous hook runs the same engine, blocking on it; the SQLite provider completes its asynchronous calls
/// synchronously, so this ties up no extra thread.
/// </para>
/// <para>
/// What no save interceptor sees, this one included: bulk <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> and raw SQL, which write
/// past the change tracker, and the bare contexts PUCK issuance mints ids on (<c>PlainfraStoreContextFactory</c>), which
/// carry no interceptors and save no domain state.
/// </para>
/// </remarks>
public sealed class StateInterceptor(StateEngine engine, StateNoteSyncService noteSync) : SaveChangesInterceptor
{
	private readonly ConcurrentDictionary<Guid, StateNoteWork> _pending = new();

	/// <inheritdoc />
	public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
	{
		if (eventData.Context is PlainfraContext context)
		{
			Hold(context, engine.RunAsync(context, CancellationToken.None).GetAwaiter().GetResult());
		}

		return base.SavingChanges(eventData, result);
	}

	/// <inheritdoc />
	public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
		DbContextEventData eventData,
		InterceptionResult<int> result,
		CancellationToken cancellationToken = default)
	{
		if (eventData.Context is PlainfraContext context)
		{
			Hold(context, await engine.RunAsync(context, cancellationToken));
		}

		return await base.SavingChangesAsync(eventData, result, cancellationToken);
	}

	/// <inheritdoc />
	public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
	{
		if (Release(eventData.Context) is { } work)
		{
			noteSync.ApplyAsync(work, CancellationToken.None).GetAwaiter().GetResult();
			ForgetDrained(eventData.Context!, work);
		}

		return base.SavedChanges(eventData, result);
	}

	/// <inheritdoc />
	public override async ValueTask<int> SavedChangesAsync(
		SaveChangesCompletedEventData eventData,
		int result,
		CancellationToken cancellationToken = default)
	{
		if (Release(eventData.Context) is { } work)
		{
			await noteSync.ApplyAsync(work, cancellationToken);
			ForgetDrained(eventData.Context!, work);
		}

		return await base.SavedChangesAsync(eventData, result, cancellationToken);
	}

	/// <inheritdoc />
	public override void SaveChangesFailed(DbContextErrorEventData eventData)
	{
		Release(eventData.Context);
		base.SaveChangesFailed(eventData);
	}

	/// <inheritdoc />
	public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
	{
		Release(eventData.Context);
		return base.SaveChangesFailedAsync(eventData, cancellationToken);
	}

	private void Hold(PlainfraContext context, StateNoteWork work)
	{
		if (work.IsEmpty)
		{
			_pending.TryRemove(context.ContextId.InstanceId, out _);
			return;
		}

		_pending[context.ContextId.InstanceId] = work;
	}

	/// <summary>
	/// The note work drains the save's reconcile intents on a scope of its own, which clears their rows, while the saving
	/// context still tracks them as saved. A later write of the same item on that context would then update a row that is
	/// gone, so the context lets them go; one the drain could not clear is still in the store, where the next write finds it.
	/// </summary>
	private static void ForgetDrained(DbContext context, StateNoteWork work)
	{
		if (work.Reconciles.Count == 0)
		{
			return;
		}

		var drained = work.Reconciles.OfType<IPuckNamedEntity>().ToList();
		foreach (var entry in context.ChangeTracker.Entries<VaultWriteIntent>().Where(entry => entry.State == EntityState.Unchanged).ToList())
		{
			if (drained.Any(entity => string.Equals(entity.GetType().FullName, entry.Entity.EntityType, StringComparison.Ordinal)
				&& string.Equals(entity.Id, entry.Entity.EntityId, StringComparison.OrdinalIgnoreCase)))
			{
				entry.State = EntityState.Detached;
			}
		}
	}

	private StateNoteWork? Release(DbContext? context)
		=> context is PlainfraContext plainfra && _pending.TryRemove(plainfra.ContextId.InstanceId, out var work) ? work : null;
}
