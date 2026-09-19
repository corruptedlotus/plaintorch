using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pleiades.Saga;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Api.Changes;

/// <summary>
/// Invalidates the <see cref="LoreActiveCache"/> whenever a lore page is written, so an edit that shifts the
/// active spine is reflected on the next read.
/// </summary>
/// <remarks>
/// Sits on the EF save hook rather than the endpoints, following the same reasoning as
/// <see cref="PlaintorchChangeFeedInterceptor"/>: every write pathway — API, watcher sync, CLI, scheduler —
/// passes through here, so a markdown edit that reshapes the spine invalidates the cache alongside an API edit.
/// Whether a lore page changed is collected before the save (afterwards the entries have reverted to Unchanged)
/// and acted on only once the save has committed.
/// </remarks>
public sealed class LoreActiveCacheInterceptor(LoreActiveCache cache) : SaveChangesInterceptor
{
	private readonly ConcurrentDictionary<Guid, bool> _loreChangedByContextId = new();

	/// <inheritdoc />
	public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
		DbContextEventData eventData,
		InterceptionResult<int> result,
		CancellationToken cancellationToken = default)
	{
		if (eventData.Context is PlainfraContext context)
		{
			_loreChangedByContextId[context.ContextId.InstanceId] = HasLoreChange(context);
		}

		return base.SavingChangesAsync(eventData, result, cancellationToken);
	}

	/// <inheritdoc />
	public override ValueTask<int> SavedChangesAsync(
		SaveChangesCompletedEventData eventData,
		int result,
		CancellationToken cancellationToken = default)
	{
		if (eventData.Context is PlainfraContext context
			&& _loreChangedByContextId.TryRemove(context.ContextId.InstanceId, out var loreChanged)
			&& loreChanged)
		{
			cache.Invalidate();
		}

		return base.SavedChangesAsync(eventData, result, cancellationToken);
	}

	/// <inheritdoc />
	public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
	{
		if (eventData.Context is PlainfraContext context)
		{
			_loreChangedByContextId.TryRemove(context.ContextId.InstanceId, out _);
		}

		return base.SaveChangesFailedAsync(eventData, cancellationToken);
	}

	private static bool HasLoreChange(PlainfraContext context)
	{
		return context.ChangeTracker.Entries<LorePage>().Any(entry =>
			entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
	}
}
