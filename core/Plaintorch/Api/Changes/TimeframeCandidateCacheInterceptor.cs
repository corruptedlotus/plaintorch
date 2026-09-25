using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Materialization;
using Pleiades.Plaintorch.Preferences;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Api.Changes;

/// <summary>
/// Invalidates the <see cref="TimeframeCandidateCache"/> whenever a write may change the active cycle's timeframe
/// candidates (PEP100 patch 2), so the next active-timeframes read recomputes them.
/// </summary>
/// <remarks>
/// <para>
/// Sits on the EF save hook for the same reason as <see cref="LoreActiveCacheInterceptor"/>: every write pathway —
/// API, watcher sync, CLI, scheduler — passes through here, so a cycle begun or ended by editing its note's
/// frontmatter invalidates the cache exactly as an API begin does. Whether a relevant change happened is collected
/// before the save (afterwards the entries have reverted to Unchanged) and acted on only once the save committed.
/// </para>
/// <para>
/// Triggers: any timeframe added, modified, or deleted; a Polaris cycle added or deleted, or with its start or end
/// modified; any directive deleted (the database cascades a lunar directive's timeframes away without a tracked
/// timeframe entry); and the default-calendar preference added, modified, or deleted (it changes how orbits read).
/// The preference trigger fires before the in-memory preference store takes the new value, so
/// <see cref="UserPreferenceService"/> invalidates once more after its store write; this hook still covers a
/// preference row written by any other pathway.
/// </para>
/// <para>
/// Orbit schedule state rows are deliberately not a trigger: a recompute persists lazily created timeframe states, and
/// invalidating on them would discard every warm it just made.
/// </para>
/// </remarks>
public sealed class TimeframeCandidateCacheInterceptor(TimeframeCandidateCache cache) : SaveChangesInterceptor
{
	private readonly ConcurrentDictionary<Guid, bool> _changedByContextId = new();

	/// <inheritdoc />
	public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
	{
		Collect(eventData);
		return base.SavingChanges(eventData, result);
	}

	/// <inheritdoc />
	public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
		DbContextEventData eventData,
		InterceptionResult<int> result,
		CancellationToken cancellationToken = default)
	{
		Collect(eventData);
		return base.SavingChangesAsync(eventData, result, cancellationToken);
	}

	/// <inheritdoc />
	public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
	{
		InvalidateIfChanged(eventData);
		return base.SavedChanges(eventData, result);
	}

	/// <inheritdoc />
	public override ValueTask<int> SavedChangesAsync(
		SaveChangesCompletedEventData eventData,
		int result,
		CancellationToken cancellationToken = default)
	{
		InvalidateIfChanged(eventData);
		return base.SavedChangesAsync(eventData, result, cancellationToken);
	}

	/// <inheritdoc />
	public override void SaveChangesFailed(DbContextErrorEventData eventData)
	{
		Forget(eventData);
		base.SaveChangesFailed(eventData);
	}

	/// <inheritdoc />
	public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
	{
		Forget(eventData);
		return base.SaveChangesFailedAsync(eventData, cancellationToken);
	}

	private void Collect(DbContextEventData eventData)
	{
		if (eventData.Context is PlainfraContext context)
		{
			_changedByContextId[context.ContextId.InstanceId] = HasCandidateChange(context);
		}
	}

	private void InvalidateIfChanged(DbContextEventData eventData)
	{
		if (eventData.Context is PlainfraContext context
			&& _changedByContextId.TryRemove(context.ContextId.InstanceId, out var changed)
			&& changed)
		{
			cache.Invalidate();
		}
	}

	private void Forget(DbContextEventData eventData)
	{
		if (eventData.Context is PlainfraContext context)
		{
			_changedByContextId.TryRemove(context.ContextId.InstanceId, out _);
		}
	}

	private static bool HasCandidateChange(PlainfraContext context)
	{
		var tracker = context.ChangeTracker;
		return tracker.Entries<Timeframe>().Any(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
			|| tracker.Entries<PolarisCycle>().Any(entry => entry.State is EntityState.Added or EntityState.Deleted
				|| (entry.State == EntityState.Modified
					&& (entry.Property(item => item.StartTime).IsModified || entry.Property(item => item.EndTime).IsModified)))
			|| tracker.Entries<Directive>().Any(entry => entry.State == EntityState.Deleted)
			|| tracker.Entries<UserPreferenceRecord>().Any(entry => (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
				&& string.Equals(entry.Entity.Key, PreferenceKeys.DefaultCalendar, StringComparison.Ordinal));
	}
}
