namespace Pleiades.Plaintorch.Materialization;

/// <summary>
/// Caches the timeframe candidates of the active Polaris cycle (PEP100 patch 2): the ids of every timeframe whose
/// orbit selects the cycle's day (a timeframe with no orbit applies to every day).
/// </summary>
/// <remarks>
/// <para>
/// Modelled on the lore active-spine cache: a singleton holding one volatile entry, recomputed on a miss and dropped
/// by <c>TimeframeCandidateCacheInterceptor</c> on every write that can change the set (timeframes, cycle begin/end,
/// directive deletes cascading timeframes away, the default-calendar preference) — and, for the calendar, once more by
/// the preference service after its in-memory store holds the new value. The entry is keyed by the cycle's
/// id and start instead of the vault session generation, because it is warmed inside vault activation — before the
/// session generation moves — and is purged explicitly when a vault is released or (re)activated.
/// </para>
/// <para>
/// Only ids are stored; the listing projects each record fresh on read, so a renamed directive or an edited window
/// never goes stale. A version counter guards the store: an <see cref="Invalidate"/> (or <see cref="Purge"/>) that
/// lands while a recompute is running bumps the version, and the recompute's then-stale result is discarded rather
/// than stored over the invalidation.
/// </para>
/// </remarks>
public sealed class TimeframeCandidateCache
{
	private readonly Lock _gate = new();
	private readonly SemaphoreSlim _compute = new(1, 1);
	private volatile Entry? _entry;
	private long _version;

	/// <summary>
	/// A cached candidate set (PEP100 patch 2).
	/// </summary>
	/// <param name="CycleId">The Polaris cycle the set was computed for.</param>
	/// <param name="CycleStart">The cycle's start instant when the set was computed; a re-begun cycle misses.</param>
	/// <param name="Day">The civil day the orbits were matched against (the cycle's local start day).</param>
	/// <param name="CandidateIds">The candidate timeframe ids, ascending.</param>
	public sealed record Entry(string CycleId, DateTimeOffset CycleStart, DateOnly Day, IReadOnlyList<long> CandidateIds);

	/// <summary>
	/// Gets the cached entry, or <see langword="null"/> when the cache is empty (never warmed, invalidated, or purged).
	/// </summary>
	public Entry? Current => _entry;

	/// <summary>
	/// Returns the cached entry when it was computed for <paramref name="cycleId"/> begun at
	/// <paramref name="cycleStart"/>, else <see langword="null"/>.
	/// </summary>
	public Entry? Find(string cycleId, DateTimeOffset cycleStart)
	{
		var entry = _entry;
		return entry is not null
			&& string.Equals(entry.CycleId, cycleId, StringComparison.Ordinal)
			&& entry.CycleStart == cycleStart
			? entry
			: null;
	}

	/// <summary>
	/// Returns the candidate ids for a cycle, reusing the cached entry when it matches unless <paramref name="force"/>
	/// is set, otherwise running <paramref name="compute"/> and storing its result — unless the cache was invalidated
	/// while it ran. Recomputes are serialized, so concurrent misses never race each other (a recompute may persist
	/// lazily created orbit states, which must not be inserted twice).
	/// </summary>
	/// <param name="cycleId">The cycle the candidates belong to.</param>
	/// <param name="cycleStart">The cycle's start instant.</param>
	/// <param name="day">The civil day the candidates are matched against.</param>
	/// <param name="compute">Computes the candidate ids from the database.</param>
	/// <param name="force">Whether to recompute even when a matching entry is cached (a warm).</param>
	/// <param name="cancellationToken">A token to cancel the wait or the recompute.</param>
	public async Task<IReadOnlyList<long>> GetOrComputeAsync(
		string cycleId,
		DateTimeOffset cycleStart,
		DateOnly day,
		Func<CancellationToken, Task<IReadOnlyList<long>>> compute,
		bool force = false,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(cycleId);
		ArgumentNullException.ThrowIfNull(compute);

		if (!force && Find(cycleId, cycleStart) is { } cached)
		{
			return cached.CandidateIds;
		}

		await _compute.WaitAsync(cancellationToken);
		try
		{
			if (!force && Find(cycleId, cycleStart) is { } computedMeanwhile)
			{
				return computedMeanwhile.CandidateIds;
			}

			long version;
			lock (_gate)
			{
				version = _version;
			}

			var ids = await compute(cancellationToken);
			lock (_gate)
			{
				if (_version == version)
				{
					_entry = new Entry(cycleId, cycleStart, day, ids);
				}
			}

			return ids;
		}
		finally
		{
			_compute.Release();
		}
	}

	/// <summary>
	/// Drops the cached candidates so the next read recomputes them — called when a write may have changed the set.
	/// </summary>
	public void Invalidate()
	{
		lock (_gate)
		{
			_version++;
			_entry = null;
		}
	}

	/// <summary>
	/// Clears the cache for a vault session boundary: a vault being released, or activated afresh, must never serve
	/// another vault's (or a previous boot's) candidates. Also discards any recompute still in flight.
	/// </summary>
	public void Purge() => Invalidate();
}
