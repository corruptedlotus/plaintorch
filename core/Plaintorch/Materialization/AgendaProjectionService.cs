using Microsoft.EntityFrameworkCore;
using Pleiades.Orbits;
using Pleiades.Orchestration;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Materialization;

/// <summary>
/// The union of projected occurrences and hardened rows over a date window (Strategy 1 / soft agenda).
/// </summary>
public sealed record AgendaProjection(IReadOnlyList<Eventive> Eventives, IReadOnlyList<Attentive> Attentives);

/// <summary>
/// The soft-agenda read model (Strategy 1 / PEP100). Computes the union of PROJECTED occurrences
/// (preview-resolved from the live schedules, trace-free — nothing is persisted) and HARDENED rows (the
/// persisted eventives/attentives, which under Strategy 1 exist only once interacted with or reached by time)
/// over a date window. A hardened row OVERRIDES its projected twin by RECURRENCE-ID, so an interacted, moved,
/// or resolved occurrence shows its persisted state, while every untouched occurrence is a pure projection
/// that always tracks the current orbit/date. This service never writes.
/// </summary>
/// <remarks>
/// Eventives dedup by <c>(owner, RecurrenceDate, RecurrenceTime)</c> — the stable iCalendar RECURRENCE-ID, so a
/// moved eventive still overrides its original slot. Attentives dedup by <c>(DecreeId, Date, Time)</c>, their
/// occurrence identity; a rescheduled orbit attentive therefore does not yet suppress its original projected
/// slot (see the attentive-recurrence-id follow-up).
/// </remarks>
public sealed class AgendaProjectionService(PlainfraContext context, PlaintorchOrbitService orbitService)
{
	/// <summary>
	/// Projects the occurrences of every active schedule over <c>[startInclusive, endInclusive]</c>, unioned
	/// with the hardened rows overlapping that window (hardened wins per recurrence-id).
	/// </summary>
	public async Task<AgendaProjection> ProjectAsync(DateOnly startInclusive, DateOnly endInclusive, CancellationToken cancellationToken = default)
	{
		var endExclusive = endInclusive.AddDays(1);
		// Occurrence positions are stored as Epoch.Moment (a datetime); the window in datetime terms is
		// [startInclusive 00:00, endExclusive 00:00).
		var windowStart = startInclusive.ToDateTime(TimeOnly.MinValue);
		var windowEndExclusive = endExclusive.ToDateTime(TimeOnly.MinValue);

		// Hardened rows overlapping the window are the source of truth for their recurrence-id.
		var eventives = await context.Eventives
			.AsNoTracking()
			.Include(item => item.Fate)
			.Include(item => item.Objective)
			.Where(item => item.Epoch.Moment >= windowStart && item.Epoch.Moment < windowEndExclusive)
			.ToListAsync(cancellationToken);
		var attentives = await context.Attentives
			.AsNoTracking()
			.Include(item => item.Decree)
			.Where(item => item.PolarisCycleId == null
				&& ((item.Epoch.Moment >= windowStart && item.Epoch.Moment < windowEndExclusive)
					|| (item.PeriodEndDate != null && item.Epoch.Moment < windowEndExclusive && item.PeriodEndDate > startInclusive)))
			.ToListAsync(cancellationToken);

		var eventiveKeys = eventives.Select(OccurrenceKey).ToHashSet();
		var attentiveKeys = attentives.Select(OccurrenceKey).ToHashSet();

		// Dated fates: a single fixed occurrence. Every projected owner (fate/decree/objective) drops its
		// auto-includes to keep the bulk read lean, then re-adds only the owning Directive explicitly — a
		// projected occurrence must still carry its owner's directive for the agenda to surface it (PEP100).
		var datedFates = await context.Fates
			.AsNoTracking()
			.IgnoreAutoIncludes()
			.Include(fate => fate.Directive)
			.Where(fate => fate.Status == FateStatus.Active && fate.Date != null
				&& fate.Date >= startInclusive && fate.Date <= endInclusive)
			.ToListAsync(cancellationToken);
		foreach (var fate in datedFates)
		{
			AddEventive(eventives, eventiveKeys, ProjectFateEventive(fate, fate.Date!.Value, fate.StartTime, fate.StartTime is null ? OrbitUnit.Day : OrbitUnit.Minute, OccurrenceDurations.SpanMinutes(fate.StartTime, fate.EndTime), fate.ResolveEventiveDuration()));
		}

		// Orbit fates: preview occurrences overlapping the window (Gregorian calendar).
		var orbitFates = await context.Fates
			.AsNoTracking()
			.IgnoreAutoIncludes()
			.Include(fate => fate.Directive)
			.Where(fate => fate.Status == FateStatus.Active && fate.Orbit != null)
			.ToListAsync(cancellationToken);
		foreach (var fate in orbitFates)
		{
			foreach (var occurrence in await orbitService.PreviewOccurrencesAsync(fate, fate.Orbit!, startInclusive, endExclusive, cancellationToken))
			{
				AddEventive(eventives, eventiveKeys, ProjectFateEventive(
					fate,
					occurrence.Date,
					occurrence.StartTime ?? fate.StartTime,
					occurrence.Granularity,
					occurrence.DurationMinutes,
					occurrence.DurationMinutes ?? fate.ResolveEventiveDuration()));
			}
		}

		// Due objectives: a single all-day occurrence on the due date.
		var dueObjectives = await context.Objectives
			.AsNoTracking()
			.IgnoreAutoIncludes()
			.Include(objective => objective.Directive)
			.Where(objective => objective.Due != null
				&& objective.Due >= startInclusive && objective.Due <= endInclusive
				&& objective.Status != ObjectiveStatus.Done
				&& objective.Status != ObjectiveStatus.Archived
				&& objective.Status != ObjectiveStatus.Failed)
			.ToListAsync(cancellationToken);
		foreach (var objective in dueObjectives)
		{
			var eventive = new Eventive
			{
				ObjectiveId = objective.Id,
				Objective = objective,
				Epoch = Epoch.From(objective.Due!.Value, timeOfDay: null, OrbitUnit.Day),
				RecurrenceDate = objective.Due!.Value,
			};
			eventive.Normalize();
			AddEventive(eventives, eventiveKeys, eventive);
		}

		// Orbit decrees materialize unbound attentives; lunar reflect-decrees are cycle-bound (reflectives),
		// not agenda attentives, so they are skipped here.
		var orbitDecrees = await context.Decrees
			.AsNoTracking()
			.IgnoreAutoIncludes()
			.Include(decree => decree.Directive)
			.Where(decree => decree.Status == DecreeStatus.Active && decree.Orbit != null)
			.ToListAsync(cancellationToken);
		foreach (var decree in orbitDecrees)
		{
			if (decree.Reflect && await orbitService.IsInLunarHierarchyAsync(decree.DirectiveId, cancellationToken))
			{
				continue;
			}

			foreach (var occurrence in await orbitService.PreviewOccurrencesAsync(decree, decree.Orbit!, startInclusive, endExclusive, cancellationToken))
			{
				var attentive = new Attentive
				{
					DecreeId = decree.Id,
					Decree = decree,
					Epoch = Epoch.From(occurrence.Date, occurrence.StartTime, occurrence.Granularity),
					RecurrenceDate = occurrence.Date,
					RecurrenceTime = occurrence.StartTime,
					PeriodEndDate = occurrence.PeriodEndExclusive > occurrence.Date.AddDays(1) ? occurrence.PeriodEndExclusive : null,
					Estimation = decree.DefaultLength,
				};
				attentive.Normalize();
				AddAttentive(attentives, attentiveKeys, attentive);
			}
		}

		eventives.Sort(static (left, right) => Compare(left.Epoch.Date, left.Epoch.TimeOfDay, right.Epoch.Date, right.Epoch.TimeOfDay));
		attentives.Sort(static (left, right) => Compare(left.Epoch.Date, left.Epoch.TimeOfDay, right.Epoch.Date, right.Epoch.TimeOfDay));
		return new AgendaProjection(eventives, attentives);
	}

	private static Eventive ProjectFateEventive(Fate fate, DateOnly date, TimeOnly? startTime, OrbitUnit granularity, int? spanMinutes, int? estimation)
	{
		var eventive = new Eventive
		{
			FateId = fate.Id,
			Fate = fate,
			Epoch = Epoch.From(date, startTime, granularity, spanMinutes),
			RecurrenceDate = date,
			RecurrenceTime = startTime,
			Estimation = estimation,
		};
		eventive.Normalize();
		return eventive;
	}

	private static void AddEventive(List<Eventive> list, HashSet<(string, DateOnly, TimeOnly?)> keys, Eventive eventive)
	{
		if (keys.Add(OccurrenceKey(eventive)))
		{
			list.Add(eventive);
		}
	}

	private static void AddAttentive(List<Attentive> list, HashSet<(string, DateOnly, TimeOnly?)> keys, Attentive attentive)
	{
		if (keys.Add(OccurrenceKey(attentive)))
		{
			list.Add(attentive);
		}
	}

	// The CalDAV occurrence identity — owner UID + RECURRENCE-ID — shared by eventives and attentives, so a
	// hardened row overrides its projected twin even after the occurrence's current start was rescheduled.
	private static (string, DateOnly, TimeOnly?) OccurrenceKey(IOccurrenceInstance occurrence)
		=> (occurrence.RecurrenceOwnerUid, occurrence.RecurrenceId.Date, occurrence.RecurrenceId.Time);

	private static int Compare(DateOnly leftDate, TimeOnly? leftTime, DateOnly rightDate, TimeOnly? rightTime)
	{
		var byDate = leftDate.CompareTo(rightDate);
		return byDate != 0 ? byDate : Nullable.Compare(leftTime, rightTime);
	}
}
