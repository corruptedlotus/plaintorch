using Microsoft.EntityFrameworkCore;
using Pleiades.Orbits;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Materialization;

/// <summary>
/// The union of projected occurrences and hardened rows over a date window (Strategy 1 / soft agenda).
/// </summary>
public sealed record AgendaProjection(IReadOnlyList<Eventive> Eventives, IReadOnlyList<Attentive> Attentives, IReadOnlyList<Terminative> Terminatives);

/// <summary>
/// The soft-agenda read model (Strategy 1 / PEP100). Computes the union of PROJECTED occurrences
/// (preview-resolved from the live schedules, trace-free — nothing is persisted) and HARDENED rows (the
/// persisted eventives/attentives/terminatives, which under Strategy 1 exist only once interacted with or reached by time)
/// over a date window. A hardened row OVERRIDES its projected twin by RECURRENCE-ID, so an interacted, moved,
/// or resolved occurrence shows its persisted state, while every untouched occurrence is a pure projection
/// that always tracks the current orbit/date. This service never writes.
/// </summary>
/// <remarks>
/// Eventives and attentives alike dedup by <c>(owner UID, RecurrenceId)</c> — the stable iCalendar identity of an
/// <see cref="Occurrence"/> — so a moved occurrence still overrides its original projected slot. A manual eventive
/// (no RecurrenceId) is always a hardened row with no projected twin, so it simply stands as itself. A terminative dedups
/// by its origin alone, since an origin has one (PEP103 patch 7).
/// </remarks>
public sealed class AgendaProjectionService(PlainfraContext context, PlaintorchOrbitService orbitService, TerminativeService terminativeService)
{
	/// <summary>
	/// The longest period one orbit occurrence spans: a year-granularity window. A hardened attentive that starts
	/// earlier than this before a window cannot reach into it.
	/// </summary>
	private const int LongestPeriodDays = 366;

	/// <summary>
	/// Projects the occurrences of every active schedule over <c>[startInclusive, endInclusive]</c>, unioned
	/// with the hardened rows overlapping that window (hardened wins per recurrence-id). A fate or decree whose stored
	/// orbit no longer parses or builds projects nothing and is logged (<see cref="PlaintorchOrbitService.IsolateAsync"/>);
	/// its hardened rows still stand.
	/// </summary>
	public async Task<AgendaProjection> ProjectAsync(DateOnly startInclusive, DateOnly endInclusive, CancellationToken cancellationToken = default)
	{
		var endExclusive = endInclusive.AddDays(1);
		// Occurrence positions are stored as Epoch.Moment (a datetime); the window in datetime terms is
		// [startInclusive 00:00, endExclusive 00:00).
		var windowStart = startInclusive.ToDateTime(TimeOnly.MinValue);
		var windowEndExclusive = endExclusive.ToDateTime(TimeOnly.MinValue);

		// Hardened rows overlapping the window are the source of truth for their recurrence-id. The window is on this
		// host's clock, while a zoned eventive's stored moment is on its zone's: the query admits every moment a zone
		// apart could place in the window, and the window itself is decided on the local moment in memory.
		var hardenedEventives = await context.Eventives
			.AsNoTracking()
			.Include(item => item.Fate)
			.Where(item => item.Epoch.Moment >= windowStart.AddDays(-OccurrenceZones.ReachDays) && item.Epoch.Moment < windowEndExclusive.AddDays(OccurrenceZones.ReachDays))
			.ToListAsync(cancellationToken);
		var eventives = hardenedEventives
			.Where(item => item.Epoch.LocalMoment >= windowStart && item.Epoch.LocalMoment < windowEndExclusive)
			.ToList();
		// An attentive also overlaps the window when it starts before it and its duration (a super-day period or a
		// span) reaches into it. Its end (Epoch.EndMoment) is derived rather than stored, so SQL admits every
		// duration-bearing attentive starting within the longest period before the window and the overlap itself
		// is decided in memory, on this host's clock like the eventives.
		var lookbackStart = windowStart.AddDays(-LongestPeriodDays);
		var hardenedAttentives = await context.Attentives
			.AsNoTracking()
			.Include(item => item.Decree)
			.Where(item => item.Epoch.Moment < windowEndExclusive.AddDays(OccurrenceZones.ReachDays)
				&& (item.Epoch.Moment >= windowStart.AddDays(-OccurrenceZones.ReachDays) || (item.Epoch.Duration != null && item.Epoch.Moment >= lookbackStart)))
			.ToListAsync(cancellationToken);
		var attentives = hardenedAttentives
			.Where(item => item.Epoch.LocalMoment < windowEndExclusive
				&& (item.Epoch.LocalMoment >= windowStart || item.Epoch.LocalEndMoment > windowStart))
			.ToList();

		// Every hardened row found shadows its projected twin, even one whose own moment falls just outside the window
		// once read on this host's clock.
		var eventiveKeys = hardenedEventives.Select(OccurrenceKey).ToHashSet();
		var attentiveKeys = hardenedAttentives.Select(OccurrenceKey).ToHashSet();

		// Orbit fates: preview occurrences overlapping the window (Gregorian calendar). A fate is orbit-only (PEP111),
		// so a one-off is just a fixed-datetime Z{…} occurrence resolved here alongside recurrences. Every projected
		// owner drops its auto-includes to keep the bulk read lean, then re-adds only the owning Directive explicitly
		// so the agenda can surface it (PEP100). An opted-out fate keeps generating, so its occurrences are still
		// projected — stamped OptOut so the display agenda hides them while they can still be interacted with or
		// opted back in (PEP100/PEP111). Each occurrence is stamped with its declarative's zone, and the preview keeps
		// those that fall in the window on this host's clock. Only owners whose temporal activation is on are projected
		// (PEP103 patch 6): a stopped or passed fate (PEP103 I-191) and an abandoned decree project nothing, nor does the due of
		// an item finished or set aside. The occurrences a fate hardened, passing ones included, still stand as hardened rows.
		var activeFates = TemporalActivation.Statuses<FateStatus>();
		var orbitFates = await context.Fates
			.AsNoTracking()
			.IgnoreAutoIncludes()
			.Include(fate => fate.Directive)
			.Where(fate => activeFates.Contains(fate.Status) && fate.Orbit != null)
			.ToListAsync(cancellationToken);
		// An owner whose orbit no longer resolves is logged and projects nothing; the rest of the agenda stands (PEP103 I-124).
		foreach (var fate in orbitFates)
		{
			var resolution = fate.Status == FateStatus.OptOut ? EventiveResolution.OptOut : EventiveResolution.Pending;
			await orbitService.IsolateAsync(fate, "the agenda projection", async () =>
			{
				foreach (var occurrence in await orbitService.PreviewOccurrencesAsync(fate, fate.Orbit!, startInclusive, endExclusive, cancellationToken))
				{
					AddEventive(eventives, eventiveKeys, new Eventive
					{
						FateId = fate.Id,
						Fate = fate,
						Epoch = PlaintorchOrbitService.EpochOf(fate, occurrence),
						RecurrenceId = occurrence.Moment,
						Resolution = resolution,
					});
				}
			});
		}

		var terminatives = await ProjectTerminativesAsync(windowStart, windowEndExclusive, cancellationToken);

		// Orbit decrees materialize unbound attentives; reflect-decrees are cycle-bound (reflectives), not agenda
		// attentives, so they are left out here.
		var activeDecrees = TemporalActivation.Statuses<DecreeStatus>();
		var orbitDecrees = await context.Decrees
			.AsNoTracking()
			.IgnoreAutoIncludes()
			.Include(decree => decree.Directive)
			.Where(decree => activeDecrees.Contains(decree.Status) && decree.Orbit != null && !decree.Reflect)
			.ToListAsync(cancellationToken);
		foreach (var decree in orbitDecrees)
		{
			await orbitService.IsolateAsync(decree, "the agenda projection", async () =>
			{
				foreach (var occurrence in await orbitService.PreviewOccurrencesAsync(decree, decree.Orbit!, startInclusive, endExclusive, cancellationToken))
				{
					var attentive = new Attentive
					{
						DecreeId = decree.Id,
						Decree = decree,
						Epoch = PlaintorchOrbitService.EpochOf(decree, occurrence),
						RecurrenceId = occurrence.Moment,
					};
					AddAttentive(attentives, attentiveKeys, attentive);
				}
			});
		}

		// Ordered as they fall on this host's clock, so a zoned occurrence takes its place by the instant it names.
		eventives.Sort(static (left, right) => Compare(left.Epoch.LocalDate, left.Epoch.LocalTimeOfDay, right.Epoch.LocalDate, right.Epoch.LocalTimeOfDay));
		attentives.Sort(static (left, right) => Compare(left.Epoch.LocalDate, left.Epoch.LocalTimeOfDay, right.Epoch.LocalDate, right.Epoch.LocalTimeOfDay));
		return new AgendaProjection(eventives, attentives, terminatives);
	}

	/// <summary>
	/// Projects the terminatives of every due that acts and falls in <c>[windowStart, windowEndExclusive)</c> on this
	/// host's clock — or anywhere before its end when <paramref name="windowStart"/> is <see langword="null"/>, as the agenda
	/// reads overdue ones — unioned with the hardened terminatives there, a hardened one standing in for its origin's
	/// projection (PEP103 patch 7). Each carries its origin, an objective with its directive and a stellar directive with its
	/// parent, ordered as they fall.
	/// </summary>
	/// <remarks>
	/// A projected terminative is pending, since only a due that acts projects. A hardened one shows as the engine keeps it:
	/// on its origin's due, resolved once the origin is finished or set aside, and flagged when the user ignores it.
	/// </remarks>
	public async Task<List<Terminative>> ProjectTerminativesAsync(DateTime? windowStart, DateTime windowEndExclusive, CancellationToken cancellationToken = default)
	{
		// As with eventives, the store is read a zone's reach either side and the window decided on the local moment.
		var hardenedQuery = context.Terminatives
			.AsNoTracking()
			.Include(item => item.Objective)
			.Include(item => item.Directive)
			.ThenInclude(directive => directive!.ParentDirective)
			.Include(item => item.Checkpoint)
			.Where(item => item.Epoch.Moment < windowEndExclusive.AddDays(OccurrenceZones.ReachDays));
		if (windowStart is { } hardenedFrom)
		{
			hardenedQuery = hardenedQuery.Where(item => item.Epoch.Moment >= hardenedFrom.AddDays(-OccurrenceZones.ReachDays));
		}

		var hardened = await hardenedQuery.ToListAsync(cancellationToken);
		var origins = hardened.Select(item => item.RecurrenceOwnerUid).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var terminatives = hardened.ToList();

		var acting = await terminativeService.FindActingAsync(
			context,
			windowStart?.AddDays(-OccurrenceZones.ReachDays),
			windowEndExclusive.AddDays(OccurrenceZones.ReachDays),
			tracking: false,
			cancellationToken);
		foreach (var origin in acting)
		{
			if (origins.Add(origin.Id))
			{
				terminatives.Add(Projected(origin));
			}
		}

		return terminatives
			.Where(item => item.Epoch.LocalMoment < windowEndExclusive && (windowStart is not { } from || item.Epoch.LocalMoment >= from))
			.OrderBy(item => item.Epoch.LocalDate)
			.ThenBy(item => item.Epoch.LocalTimeOfDay)
			.ToList();
	}

	/// <summary>
	/// The terminative an untracked origin projects, carrying the origin for the agenda to draw.
	/// </summary>
	private Terminative Projected(IDueOwner origin)
	{
		var terminative = terminativeService.Spawn(origin);
		switch (origin)
		{
			case Objective objective:
				terminative.Objective = objective;
				break;
			case StellarDirective directive:
				terminative.Directive = directive;
				break;
			case Checkpoint checkpoint:
				terminative.Checkpoint = checkpoint;
				break;
		}

		return terminative;
	}

	private static void AddEventive(List<Eventive> list, HashSet<(string, DateTime?)> keys, Eventive eventive)
	{
		if (keys.Add(OccurrenceKey(eventive)))
		{
			list.Add(eventive);
		}
	}

	private static void AddAttentive(List<Attentive> list, HashSet<(string, DateTime?)> keys, Attentive attentive)
	{
		if (keys.Add(OccurrenceKey(attentive)))
		{
			list.Add(attentive);
		}
	}

	// The CalDAV occurrence identity — owner UID + RECURRENCE-ID — shared by eventives and attentives, so a
	// hardened row overrides its projected twin even after the occurrence's current start was rescheduled. Only
	// projected occurrences are added through the key check, and they always carry a RECURRENCE-ID, so the null key
	// of a manual eventive never shadows one.
	private static (string, DateTime?) OccurrenceKey(Occurrence occurrence)
		=> (occurrence.RecurrenceOwnerUid, occurrence.RecurrenceId);

	private static int Compare(DateOnly leftDate, TimeOnly? leftTime, DateOnly rightDate, TimeOnly? rightTime)
	{
		var byDate = leftDate.CompareTo(rightDate);
		return byDate != 0 ? byDate : Nullable.Compare(leftTime, rightTime);
	}
}
