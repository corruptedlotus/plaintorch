using Microsoft.EntityFrameworkCore;
using Pleiades.Orbits;
using Pleiades.Orchestration;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch;

/// <summary>
/// Bridges the declarative ecosystem to the Orbit engine (PEP100).
/// </summary>
/// <remarks>
/// <para>
/// Two resolution modes are used deliberately. <b>Seeking</b> (<see cref="SeekOccurrencesAsync"/>) advances
/// and re-persists the per-declarative schedule state, consuming every pending occurrence up to the window
/// end — so already-resolved (and possibly modified) instances are bypassed by their occurrence identity
/// rather than resolved again. <b>Preview</b> (<see cref="MatchesDayAsync"/>,
/// <see cref="PreviewDayOccurrencesAsync"/>) is trace-free and never pushes the state forward; it validates
/// interaction with single — possibly future — occurrences.
/// </para>
/// <para>
/// The resolver calendar is modular per declarative kind: decree orbits (attentives) and reflective
/// day-matching resolve on the <b>Pleiadean</b> calendar by default, matching the system's mechanics;
/// fate orbits (eventives) resolve on the <b>Gregorian</b> calendar.
/// </para>
/// </remarks>
public sealed class PlaintorchOrbitService(PlainfraContext context)
{
	/// <summary>
	/// Resolves the calendar an incentive's orbit is resolved against: Pleiadean for decrees (attentives and
	/// reflectives), Gregorian for fates (eventives).
	/// </summary>
	public static IOrbitCalendar ResolveCalendar(Incentive incentive)
	{
		ArgumentNullException.ThrowIfNull(incentive);

		// An explicit per-declarative calendar wins; otherwise fall back to the kind default
		// (decrees Pleiadean, fates Gregorian) until a user preference (PEP116) sets one.
		if (incentive is Declarative { Calendar: { } calendar })
		{
			return calendar == DeclarativeCalendar.Pleiadean ? OrbitDays.Pleiadean : OrbitDays.Gregorian;
		}

		return incentive is Decree ? OrbitDays.Pleiadean : OrbitDays.Gregorian;
	}

	/// <summary>
	/// Validates a fate orbit: it must parse. Fates may use any granularity, and span-format orbits
	/// (<c>=&lt;dur&gt;</c>) are welcome — the span supplies the eventive length, so no duration override
	/// is needed for orbit-based fates.
	/// </summary>
	/// <exception cref="ArgumentException">The notation is invalid.</exception>
	public static void ValidateFateOrbit(string? orbit)
	{
		Validate(orbit, OrbitDays.ValidateParses);
	}

	/// <summary>
	/// Validates a decree orbit: it must parse and be granular (no spans; lengths come from the decree's
	/// default length). Any granularity is allowed — sub-day occurrences carry a time of day, and super-day
	/// occurrences span periods multiple Polaris cycles can collide with. When the decree reflects
	/// (<paramref name="reflect"/>), the orbit must additionally resolve at day granularity.
	/// </summary>
	/// <exception cref="ArgumentException">The notation is invalid for the decree's use.</exception>
	public static void ValidateDecreeOrbit(string? orbit, bool reflect)
	{
		Validate(orbit, reflect ? OrbitDays.ValidateDayGranularity : OrbitDays.ValidateGranular);
	}

	/// <summary>
	/// Validates a timeframe orbit: day granularity (it selects the days a timeframe applies to).
	/// </summary>
	/// <exception cref="ArgumentException">The notation is invalid.</exception>
	public static void ValidateTimeframeOrbit(string? orbit)
	{
		Validate(orbit, OrbitDays.ValidateDayGranularity);
	}

	private static void Validate(string? orbit, Action<string> validator)
	{
		if (string.IsNullOrWhiteSpace(orbit))
		{
			return;
		}

		try
		{
			validator(orbit);
		}
		catch (FormatException exception)
		{
			throw new ArgumentException($"Invalid orbit notation '{orbit}': {exception.Message}", exception);
		}
	}

	/// <summary>
	/// Resets the persisted schedule state for a declarative after its orbit was assigned, changed, or
	/// cleared. A new state anchored at <paramref name="epoch"/> is created when an orbit is present.
	/// </summary>
	public async Task ResetStateAsync(Incentive incentive, string? orbit, DateOnly epoch, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(incentive);
		var existing = await context.OrbitScheduleStates
			.Where(state => state.IncentiveId == incentive.Id)
			.ToListAsync(cancellationToken);
		if (existing.Count > 0)
		{
			context.OrbitScheduleStates.RemoveRange(existing);
		}

		if (!string.IsNullOrWhiteSpace(orbit))
		{
			context.OrbitScheduleStates.Add(new OrbitScheduleState
			{
				IncentiveId = incentive.Id,
				StateJson = OrbitDays.CreateState(orbit, epoch, ResolveCalendar(incentive)),
				UpdatedUtc = DateTimeOffset.UtcNow,
			});
		}
	}

	/// <summary>
	/// SEEKING: resolves and consumes every pending occurrence of the declarative's orbit up to
	/// <paramref name="endExclusive"/> (catch-up included), advancing and persisting the schedule state.
	/// </summary>
	public async Task<IReadOnlyList<OrbitOccurrenceInstance>> SeekOccurrencesAsync(
		Incentive incentive, string orbit, DateOnly endExclusive, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(incentive);
		var state = await GetOrCreateStateAsync(incentive, orbit, DateOnly.FromDateTime(DateTime.Today), cancellationToken);
		var (occurrences, advanced) = OrbitDays.SeekOccurrencesThrough(state.StateJson, endExclusive, ResolveCalendar(incentive));
		state.StateJson = advanced;
		state.UpdatedUtc = DateTimeOffset.UtcNow;
		return occurrences;
	}

	/// <summary>
	/// SEEKING to an instant: resolves and consumes every pending occurrence of the declarative's orbit strictly
	/// before <paramref name="endExclusive"/> (an instant, not a whole day), advancing and persisting the
	/// schedule state. This is the harden-on-time catch-up — it hardens occurrences whose time has already
	/// arrived without consuming the still-future occurrences of the same day.
	/// </summary>
	public async Task<IReadOnlyList<OrbitOccurrenceInstance>> SeekOccurrencesThroughInstantAsync(
		Incentive incentive, string orbit, DateTime endExclusive, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(incentive);
		var state = await GetOrCreateStateAsync(incentive, orbit, DateOnly.FromDateTime(DateTime.Today), cancellationToken);
		var (occurrences, advanced) = OrbitDays.SeekOccurrencesThroughInstant(state.StateJson, endExclusive, ResolveCalendar(incentive));
		state.StateJson = advanced;
		state.UpdatedUtc = DateTimeOffset.UtcNow;
		return occurrences;
	}

	/// <summary>
	/// PREVIEW: the declarative's orbit occurrences whose period covers the given day, without pushing the
	/// schedule state forward.
	/// </summary>
	public async Task<IReadOnlyList<OrbitOccurrenceInstance>> PreviewDayOccurrencesAsync(
		Incentive incentive, string orbit, DateOnly day, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(incentive);
		var today = DateOnly.FromDateTime(DateTime.Today);
		var state = await GetOrCreateStateAsync(incentive, orbit, day < today ? day : today, cancellationToken);
		var calendar = ResolveCalendar(incentive);
		return OrbitDays.PreviewOccurrencesWithin(state.StateJson, day.AddDays(-400), day.AddDays(1), calendar)
			.Where(occurrence => occurrence.Date <= day && day < occurrence.PeriodEndExclusive)
			.ToList();
	}

	/// <summary>
	/// PREVIEW: whether the declarative's orbit has an occurrence covering the given day.
	/// </summary>
	public async Task<bool> MatchesDayAsync(Incentive incentive, string orbit, DateOnly day, CancellationToken cancellationToken = default)
	{
		return (await PreviewDayOccurrencesAsync(incentive, orbit, day, cancellationToken)).Count > 0;
	}

	/// <summary>
	/// PREVIEW: the declarative's orbit occurrences whose period overlaps <c>[startInclusive, endExclusive)</c>,
	/// resolved from the epoch without pushing the schedule state forward. A super-day occurrence whose period
	/// began before the window is included, so weekly/monthly instances that merely span the window are caught.
	/// </summary>
	public async Task<IReadOnlyList<OrbitOccurrenceInstance>> PreviewOccurrencesAsync(
		Incentive incentive, string orbit, DateOnly startInclusive, DateOnly endExclusive, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(incentive);
		var today = DateOnly.FromDateTime(DateTime.Today);
		// Look back far enough that a super-day period starting earlier can still cover the window.
		var lookbackStart = startInclusive.AddDays(-400);
		var state = await GetOrCreateStateAsync(incentive, orbit, lookbackStart < today ? lookbackStart : today, cancellationToken);
		return OrbitDays.PreviewOccurrencesWithin(state.StateJson, lookbackStart, endExclusive, ResolveCalendar(incentive))
			.Where(occurrence => occurrence.Date < endExclusive && occurrence.PeriodEndExclusive > startInclusive)
			.ToList();
	}

	/// <summary>
	/// Determines whether a directive lineage belongs to a Moonlight (lunar) hierarchy — i.e. the directive
	/// itself or any ancestor is a lunar directive.
	/// </summary>
	public async Task<bool> IsInLunarHierarchyAsync(string? directiveId, CancellationToken cancellationToken = default)
	{
		var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var currentId = directiveId;
		while (!string.IsNullOrWhiteSpace(currentId) && visited.Add(currentId))
		{
			var directive = await context.Directives
				.AsNoTracking()
				.FirstOrDefaultAsync(item => item.Id == currentId, cancellationToken);
			if (directive is null)
			{
				return false;
			}

			if (directive is LunarDirective)
			{
				return true;
			}

			currentId = directive.ParentDirectiveId;
		}

		return false;
	}

	/// <summary>
	/// Loads the declarative's persisted schedule state, lazily creating one (anchored at
	/// <paramref name="fallbackEpoch"/>) when the orbit has never been resolved before.
	/// The lazy row is what pins the schedule's random seed, so previews stay deterministic.
	/// </summary>
	private async Task<OrbitScheduleState> GetOrCreateStateAsync(Incentive incentive, string orbit, DateOnly fallbackEpoch, CancellationToken cancellationToken)
	{
		var state = await context.OrbitScheduleStates
			.FirstOrDefaultAsync(item => item.IncentiveId == incentive.Id, cancellationToken);
		if (state is not null)
		{
			return state;
		}

		state = new OrbitScheduleState
		{
			IncentiveId = incentive.Id,
			StateJson = OrbitDays.CreateState(orbit, fallbackEpoch, ResolveCalendar(incentive)),
			UpdatedUtc = DateTimeOffset.UtcNow,
		};
		context.OrbitScheduleStates.Add(state);
		return state;
	}
}
