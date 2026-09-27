using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Materialization;

/// <summary>
/// Computes and caches the timeframe candidates of a begun Polaris cycle (PEP100 patch 2): every timeframe without an
/// orbit, plus every timeframe whose orbit selects the cycle's day on the vault default calendar.
/// </summary>
/// <remarks>
/// The cycle's day is its local start day (<see cref="CycleDay"/>), the same day cycle-begin reflectives are matched
/// against, and a cycle's candidate set is fixed for its whole life — even when it stays open past midnight. The set
/// lives in the singleton <see cref="TimeframeCandidateCache"/>; this scoped service owns the database side (it needs
/// the unit of work and the scoped calendar preference), warming it when a cycle begins and at vault activation and
/// recomputing lazily on a miss. Pinning lazily created orbit states is best effort: when a concurrent write makes
/// that save fail, the states are dropped from the unit of work and the computed candidates are still returned.
/// </remarks>
public sealed class TimeframeCandidateService(
	PlainfraContext context,
	PlaintorchOrbitService orbitService,
	TimeframeCandidateCache cache,
	ILogger<TimeframeCandidateService> logger)
{
	/// <summary>
	/// Recomputes the candidates of a begun cycle and stores them in the cache (a warm), saving any timeframe orbit
	/// state created lazily on the way so its seed stays pinned. Returns the candidate ids.
	/// </summary>
	/// <exception cref="InvalidOperationException">The cycle has not begun.</exception>
	public Task<IReadOnlyList<long>> RefreshAsync(PolarisCycle cycle, CancellationToken cancellationToken = default)
		=> GetAsync(cycle, force: true, cancellationToken);

	/// <summary>
	/// Returns the candidate ids of the given active cycle: the cached set when it was computed for this cycle and
	/// start, else a fresh computation (which is cached in turn).
	/// </summary>
	/// <exception cref="InvalidOperationException">The cycle has not begun.</exception>
	public Task<IReadOnlyList<long>> GetCandidateIdsAsync(PolarisCycle activeCycle, CancellationToken cancellationToken = default)
		=> GetAsync(activeCycle, force: false, cancellationToken);

	/// <summary>
	/// Resolves the civil day a begun cycle's timeframe orbits are matched against: its local start day.
	/// </summary>
	/// <exception cref="InvalidOperationException">The cycle has not begun.</exception>
	public static DateOnly CycleDay(PolarisCycle cycle)
	{
		ArgumentNullException.ThrowIfNull(cycle);
		var start = cycle.StartTime
			?? throw new InvalidOperationException($"Polaris cycle '{cycle.Id}' has not begun, so it has no timeframe candidates.");
		return DateOnly.FromDateTime(start.LocalDateTime);
	}

	private Task<IReadOnlyList<long>> GetAsync(PolarisCycle cycle, bool force, CancellationToken cancellationToken)
	{
		var day = CycleDay(cycle);
		return cache.GetOrComputeAsync(
			cycle.Id,
			cycle.StartTime!.Value,
			day,
			token => ComputeAsync(day, token),
			force,
			cancellationToken);
	}

	private async Task<IReadOnlyList<long>> ComputeAsync(DateOnly day, CancellationToken cancellationToken)
	{
		var timeframes = await context.Timeframes
			.AsNoTracking()
			.OrderBy(timeframe => timeframe.Id)
			.ToListAsync(cancellationToken);

		var candidates = await orbitService.MatchTimeframesDayAsync(timeframes, day, cancellationToken);

		// A timeframe whose orbit predates timeframe states got one lazily; persisting it pins its epoch and seed, so
		// later cycles read the orbit the same way.
		var lazyStates = context.ChangeTracker.Entries<TimeframeOrbitScheduleState>()
			.Where(entry => entry.State == EntityState.Added)
			.ToList();
		if (lazyStates.Count > 0)
		{
			try
			{
				await context.SaveChangesAsync(cancellationToken);
			}
			catch (DbUpdateException exception)
			{
				// A concurrent write won the race: the timeframe was deleted (foreign key) or its orbit was edited and the
				// state-policy reset inserted a fresh state first (unique timeframe index). The candidates computed here
				// are still right for this read, and the next computation reads whichever state is stored by then.
				foreach (var entry in lazyStates)
				{
					entry.State = EntityState.Detached;
				}

				logger.LogWarning(exception, "PLAINTORCH could not pin {Count} lazily created timeframe orbit state(s) for {Day}; a concurrent write got there first.", lazyStates.Count, day);
			}
		}

		return candidates;
	}
}
