using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pleiades.Orbits;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Preferences;
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
/// The resolver calendar is modular: an explicit per-declarative calendar wins, otherwise the vault's global
/// default-calendar preference (PEP116) decides — a single default, no longer a fate-vs-decree split.
/// </para>
/// <para>
/// Timeframe orbits (PEP100 patch 2) are anchored by their own persisted <see cref="TimeframeOrbitScheduleState"/>
/// and are only ever previewed against a day (<see cref="MatchesTimeframeDayAsync"/>), always on the vault default
/// calendar (<see cref="ResolveDefaultCalendar"/>).
/// </para>
/// </remarks>
public sealed class PlaintorchOrbitService(
	PlainfraContext context,
	IOptionsSnapshot<AgendaPreferences> agendaPreferences,
	ILogger<PlaintorchOrbitService> logger)
{
	/// <summary>
	/// Resolves the calendar an incentive's orbit is resolved against: an explicit per-declarative
	/// <see cref="Declarative.Calendar"/> when set, otherwise the vault-wide default from
	/// <see cref="AgendaPreferences.DefaultCalendar"/> (PEP116).
	/// </summary>
	public IOrbitCalendar ResolveCalendar(Incentive incentive)
	{
		ArgumentNullException.ThrowIfNull(incentive);

		// An explicit per-declarative calendar wins; otherwise the vault's global default preference decides.
		var calendar = incentive is Declarative { Calendar: { } explicitCalendar }
			? explicitCalendar
			: agendaPreferences.Value.DefaultCalendar;

		return calendar == DeclarativeCalendar.Pleiadean ? OrbitDays.Pleiadean : OrbitDays.Gregorian;
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
		var existing = await context.IncentiveOrbitScheduleStates
			.Where(state => state.IncentiveId == incentive.Id)
			.ToListAsync(cancellationToken);
		if (existing.Count > 0)
		{
			context.IncentiveOrbitScheduleStates.RemoveRange(existing);
		}

		if (!string.IsNullOrWhiteSpace(orbit))
		{
			context.IncentiveOrbitScheduleStates.Add(new IncentiveOrbitScheduleState
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
	/// FAST-FORWARD: advances and persists the declarative's schedule cursor to <paramref name="now"/> WITHOUT
	/// emitting the occurrences it passes. This is the resume-from-pause seek (PEP100): re-activating a cancelled
	/// fate or an abandoned decree picks generation up from now forward, so the occurrences that elapsed while it
	/// was paused are skipped rather than back-filled by the next harden-on-time pass.
	/// </summary>
	public async Task FastForwardToNowAsync(Incentive incentive, string orbit, DateTime now, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(incentive);
		var state = await GetOrCreateStateAsync(incentive, orbit, DateOnly.FromDateTime(DateTime.Today), cancellationToken);
		var (_, advanced) = OrbitDays.SeekOccurrencesThroughInstant(state.StateJson, now, ResolveCalendar(incentive));
		state.StateJson = advanced;
		state.UpdatedUtc = DateTimeOffset.UtcNow;
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
	/// Computes the declarative's next upcoming occurrence at or after <paramref name="from"/> from its persisted
	/// schedule state, without advancing it — the value cached in <see cref="Declarative.NextOccurrence"/>. Returns
	/// <see langword="null"/> when the declarative has no schedule state (unscheduled) or no occurrence within the
	/// lookahead horizon. Sees a state added earlier in the current unit of work, so it is correct mid-save.
	/// </summary>
	public async Task<DateTime?> ComputeNextOccurrenceAsync(Incentive incentive, DateTime from, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(incentive);
		var tracked = context.ChangeTracker.Entries<IncentiveOrbitScheduleState>()
			.FirstOrDefault(entry => entry.State != Microsoft.EntityFrameworkCore.EntityState.Deleted
				&& entry.Entity.IncentiveId == incentive.Id)?.Entity;
		var state = tracked
			?? await context.IncentiveOrbitScheduleStates.FirstOrDefaultAsync(item => item.IncentiveId == incentive.Id, cancellationToken);
		return state is null ? null : OrbitDays.NextOccurrence(state.StateJson, from, ResolveCalendar(incentive));
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
	private async Task<IncentiveOrbitScheduleState> GetOrCreateStateAsync(Incentive incentive, string orbit, DateOnly fallbackEpoch, CancellationToken cancellationToken)
	{
		var state = await context.IncentiveOrbitScheduleStates
			.FirstOrDefaultAsync(item => item.IncentiveId == incentive.Id, cancellationToken);
		if (state is not null)
		{
			return state;
		}

		state = new IncentiveOrbitScheduleState
		{
			IncentiveId = incentive.Id,
			StateJson = OrbitDays.CreateState(orbit, fallbackEpoch, ResolveCalendar(incentive)),
			UpdatedUtc = DateTimeOffset.UtcNow,
		};
		context.IncentiveOrbitScheduleStates.Add(state);
		return state;
	}

	/// <summary>
	/// Resolves the calendar timeframe orbits are read on: the vault-wide default from
	/// <see cref="AgendaPreferences.DefaultCalendar"/> (PEP116; Pleiadean unless changed). Timeframes carry no
	/// calendar of their own and need no incentive to resolve it (PEP100 patch 2).
	/// </summary>
	public IOrbitCalendar ResolveDefaultCalendar()
	{
		return agendaPreferences.Value.DefaultCalendar == DeclarativeCalendar.Pleiadean ? OrbitDays.Pleiadean : OrbitDays.Gregorian;
	}

	/// <summary>
	/// Resets a timeframe's persisted orbit state after its orbit was assigned, changed, or cleared (PEP100 patch 2),
	/// mirroring <see cref="ResetStateAsync"/>: any existing state is removed, and a new one anchored at
	/// <paramref name="epoch"/> on the vault default calendar is added when an orbit is present. Nothing is saved here;
	/// the change rides the caller's unit of work.
	/// </summary>
	public async Task ResetTimeframeStateAsync(Timeframe timeframe, string? orbit, DateOnly epoch, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(timeframe);

		// A state added earlier in this unit of work is not in the database yet, so the tracked ones are swept too.
		var existing = context.TimeframeOrbitScheduleStates.Local
			.Where(state => ReferenceEquals(state.Timeframe, timeframe) || (timeframe.Id != 0 && state.TimeframeId == timeframe.Id))
			.ToList();
		if (timeframe.Id != 0)
		{
			existing.AddRange(await context.TimeframeOrbitScheduleStates
				.Where(state => state.TimeframeId == timeframe.Id)
				.ToListAsync(cancellationToken));
		}

		if (existing.Count > 0)
		{
			context.TimeframeOrbitScheduleStates.RemoveRange(existing.Distinct());
		}

		if (!string.IsNullOrWhiteSpace(orbit))
		{
			context.TimeframeOrbitScheduleStates.Add(new TimeframeOrbitScheduleState
			{
				TimeframeId = timeframe.Id,
				// A timeframe created in this unit of work has no identity yet; the navigation lets EF fix the key up.
				Timeframe = timeframe.Id == 0 ? timeframe : null,
				StateJson = OrbitDays.CreateState(orbit, epoch, ResolveDefaultCalendar()),
				UpdatedUtc = DateTimeOffset.UtcNow,
			});
		}
	}

	/// <summary>
	/// PREVIEW: whether a timeframe's orbit selects the given day (PEP100 patch 2), read on the vault default calendar.
	/// A timeframe without an orbit applies to every day. The persisted state is never advanced; a timeframe that has
	/// an orbit but no state yet (data from before timeframe states existed) gets one lazily, anchored like the
	/// incentive lazy path (<paramref name="day"/> when it lies in the past, else today) — the caller saves it so the
	/// seed stays pinned. A malformed stored orbit never throws: it is logged and reads as non-matching.
	/// </summary>
	public async Task<bool> MatchesTimeframeDayAsync(Timeframe timeframe, DateOnly day, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(timeframe);
		if (string.IsNullOrWhiteSpace(timeframe.Orbit))
		{
			return true;
		}

		var state = context.TimeframeOrbitScheduleStates.Local.FirstOrDefault(item => item.TimeframeId == timeframe.Id)
			?? await context.TimeframeOrbitScheduleStates.FirstOrDefaultAsync(item => item.TimeframeId == timeframe.Id, cancellationToken);
		return MatchTimeframeDay(timeframe, day, state, ResolveDefaultCalendar(), created: null);
	}

	/// <summary>
	/// PREVIEW, batched: the ids of the given timeframes whose orbit selects <paramref name="day"/>, in input order, with
	/// exactly the per-timeframe semantics of <see cref="MatchesTimeframeDayAsync"/> (PEP100 patch 2). The timeframe
	/// states are loaded in one query and the tracked ones read in one pass, instead of a lookup per timeframe; lazily
	/// created states are added to the unit of work for the caller to save.
	/// </summary>
	public async Task<IReadOnlyList<long>> MatchTimeframesDayAsync(IReadOnlyList<Timeframe> timeframes, DateOnly day, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(timeframes);
		var matching = new List<long>(timeframes.Count);
		if (timeframes.All(timeframe => string.IsNullOrWhiteSpace(timeframe.Orbit)))
		{
			matching.AddRange(timeframes.Select(timeframe => timeframe.Id));
			return matching;
		}

		// Stored states first, then the unit of work's live (not deleted) ones on top — the same precedence as the
		// single-timeframe lookup, which prefers a tracked state over the database row.
		var states = new Dictionary<long, TimeframeOrbitScheduleState>();
		foreach (var stored in await context.TimeframeOrbitScheduleStates.ToListAsync(cancellationToken))
		{
			states.TryAdd(stored.TimeframeId, stored);
		}

		foreach (var entry in context.ChangeTracker.Entries<TimeframeOrbitScheduleState>())
		{
			if (entry.State != EntityState.Deleted)
			{
				states[entry.Entity.TimeframeId] = entry.Entity;
			}
		}

		var calendar = ResolveDefaultCalendar();
		foreach (var timeframe in timeframes)
		{
			if (string.IsNullOrWhiteSpace(timeframe.Orbit)
				|| MatchTimeframeDay(timeframe, day, states.GetValueOrDefault(timeframe.Id), calendar, created => states[timeframe.Id] = created))
			{
				matching.Add(timeframe.Id);
			}
		}

		return matching;
	}

	/// <summary>
	/// Matches one orbit-bearing timeframe against a day from its already looked-up state, lazily creating (and adding)
	/// the state when it has none. A malformed orbit or state is logged and reads as non-matching.
	/// </summary>
	private bool MatchTimeframeDay(Timeframe timeframe, DateOnly day, TimeframeOrbitScheduleState? state, IOrbitCalendar calendar, Action<TimeframeOrbitScheduleState>? created)
	{
		var orbit = timeframe.Orbit!;
		try
		{
			if (state is null)
			{
				var today = DateOnly.FromDateTime(DateTime.Today);
				state = new TimeframeOrbitScheduleState
				{
					TimeframeId = timeframe.Id,
					StateJson = OrbitDays.CreateState(orbit, day < today ? day : today, calendar),
					UpdatedUtc = DateTimeOffset.UtcNow,
				};
				context.TimeframeOrbitScheduleStates.Add(state);
				created?.Invoke(state);
			}

			return OrbitDays.MatchesDay(state.StateJson, day, calendar);
		}
		catch (Exception exception) when (exception is FormatException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
		{
			logger.LogWarning(exception, "Timeframe {TimeframeId} has an unreadable orbit '{Orbit}'; it is treated as not matching {Day}.", timeframe.Id, orbit, day);
			return false;
		}
	}
}
