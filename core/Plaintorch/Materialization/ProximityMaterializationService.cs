using Microsoft.EntityFrameworkCore;
using Pleiades.Orbits;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.State;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Materialization;

/// <summary>
/// Materializes backlog instances by proximity (PEP100): dated and orbit-scheduled fates ensure their
/// eventives, objective due dates ensure their eventives, orbit-scheduled decrees ensure their unbound
/// attentives, and lunar-hierarchy reflect-decrees generate cycle-bound reflectives.
/// </summary>
/// <remarks>
/// One materializer serves two triggers. <see cref="MaterializeForCycleAsync"/> runs when a Polaris cycle
/// begins, anchored on the cycle's 24h window and generating its bound reflectives. <see cref="MaterializeForDayAsync"/>
/// runs on the daily background pass, anchored on today and independent of any cycle, so the agenda's
/// attentives and upcoming eventives exist as rows without waiting for a cycle to be begun. Orbit resolution
/// SEEKS (advances the persisted schedule state); instance identity is the occurrence date/time, so already
/// interacted instances are recognized rather than duplicated.
/// </remarks>
public sealed class ProximityMaterializationService(
	PlainfraContext context,
	PlaintorchOrbitService orbitService,
	DependencyGateService dependencyGate,
	VaultAuditLogService auditLogService)
{
	/// <summary>
	/// How many days ahead the daily/recheck pass fills fate and objective eventives, so the agenda's
	/// upcoming list has rows to read. Decree attentives are never pre-created past today (see
	/// <see cref="MaterializeForDayAsync"/>).
	/// </summary>
	public const int DefaultEventiveHorizonDays = 7;

	/// <summary>
	/// Materializes the proximity instances for a beginning cycle: its 24h window plus the cycle-bound
	/// reflectives of matching lunar reflect-decrees. Returns the number of instances created.
	/// </summary>
	public async Task<int> MaterializeForCycleAsync(PolarisCycle cycle, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(cycle);
		if (cycle.StartTime is null)
		{
			return 0;
		}

		var (windowStart, windowEnd) = InclusionWindow.Resolve(cycle);
		var windowEndDayExclusive = DateOnly.FromDateTime(windowEnd).AddDays(windowEnd.TimeOfDay > TimeSpan.Zero ? 1 : 0);

		var created = await MaterializeCoreAsync(
			windowStart,
			windowEnd,
			fateSeekThroughExclusive: windowEndDayExclusive,
			decreeSeekThroughExclusive: windowEndDayExclusive,
			cycle: cycle,
			cancellationToken);

		if (created > 0)
		{
			await auditLogService.WriteAsync(
				"api",
				"polaris.materialize-proximity",
				subjectType: nameof(PolarisCycle),
				subjectId: cycle.Id,
				subjectTitle: cycle.Title,
				details: new { created },
				cancellationToken: cancellationToken);
		}

		return created;
	}

	/// <summary>
	/// Materializes the day's due instances independently of any cycle: unbound decree attentives caught up
	/// through today, and fate/objective eventives across the upcoming horizon so the agenda can list them.
	/// Reflect-decrees are left untouched — their reflectives are cycle-bound and belong to cycle begin.
	/// Returns the number of instances created.
	/// </summary>
	public async Task<int> MaterializeForDayAsync(DateOnly today, int eventiveHorizonDays, CancellationToken cancellationToken = default)
	{
		var windowStart = today.ToDateTime(TimeOnly.MinValue);
		var windowEnd = today.AddDays(eventiveHorizonDays).ToDateTime(new TimeOnly(23, 59, 59));

		var created = await MaterializeCoreAsync(
			windowStart,
			windowEnd,
			// Fates and due objectives look ahead to fill the agenda's upcoming list; decree attentives only
			// catch up to today, so future routine instances are not pre-created (a later orbit change would
			// otherwise strand them).
			fateSeekThroughExclusive: today.AddDays(eventiveHorizonDays + 1),
			decreeSeekThroughExclusive: today.AddDays(1),
			cycle: null,
			cancellationToken);

		if (created > 0)
		{
			await auditLogService.WriteAsync(
				"daemon",
				"daily.materialize",
				subjectType: nameof(PolarisCycle),
				details: new { created, date = today.ToString("yyyy-MM-dd") },
				cancellationToken: cancellationToken);
		}

		return created;
	}

	private async Task<int> MaterializeCoreAsync(
		DateTime windowStart,
		DateTime windowEnd,
		DateOnly fateSeekThroughExclusive,
		DateOnly decreeSeekThroughExclusive,
		PolarisCycle? cycle,
		CancellationToken cancellationToken)
	{
		var candidateDates = InclusionWindow.EnumerateDates(windowStart, windowEnd);
		var startDay = DateOnly.FromDateTime(windowStart);
		var created = 0;

		// Dated fates: collide by their explicit time specification.
		var datedFates = await context.Fates
			.AsNoTracking()
			.IgnoreAutoIncludes()
			.Where(fate => fate.Status == FateStatus.Active && fate.Date != null && candidateDates.Contains(fate.Date.Value))
			.ToListAsync(cancellationToken);

		foreach (var fate in datedFates)
		{
			if (!InclusionWindow.Intersects(fate.Date!.Value, fate.StartTime, fate.EndTime, windowStart, windowEnd))
			{
				continue;
			}

			created += await EnsureFateEventiveAsync(fate, fate.Date.Value, cancellationToken) ? 1 : 0;
		}

		// Orbit-scheduled fates: seek their (Gregorian-calendar) schedules through the window end, catching up
		// on anything pending since the last seek. Span-format orbits carry the eventive length themselves.
		var orbitFates = await context.Fates
			.IgnoreAutoIncludes()
			.Where(fate => fate.Status == FateStatus.Active && fate.Orbit != null)
			.ToListAsync(cancellationToken);

		foreach (var fate in orbitFates)
		{
			foreach (var occurrence in await orbitService.SeekOccurrencesAsync(fate, fate.Orbit!, fateSeekThroughExclusive, cancellationToken))
			{
				created += await EnsureFateEventiveAsync(fate, occurrence, cancellationToken) ? 1 : 0;
			}
		}

		// Orbit-scheduled decrees resolve on the Pleiadean calendar. Lunar reflect-decrees resolve at day
		// granularity against the cycle's start day and generate cycle-bound reflectives (cycle begin only);
		// every other decree materializes unbound attentives — timed for sub-day granularities, period-spanning
		// for super-day granularities (so multiple cycles can collide with one instance).
		var orbitDecrees = await context.Decrees
			.IgnoreAutoIncludes()
			.Where(decree => decree.Status == DecreeStatus.Active && decree.Orbit != null)
			.ToListAsync(cancellationToken);

		foreach (var decree in orbitDecrees)
		{
			var reflects = decree.Reflect && await orbitService.IsInLunarHierarchyAsync(decree.DirectiveId, cancellationToken);
			if (reflects)
			{
				// Reflectives are cycle-bound. A cycle-less (daily) pass leaves the reflect-decree entirely
				// alone — its schedule state is not advanced here — so the next cycle begin still generates it.
				if (cycle is null)
				{
					continue;
				}

				var occurrences = await orbitService.SeekOccurrencesAsync(decree, decree.Orbit!, startDay.AddDays(1), cancellationToken);
				if (occurrences.Any(occurrence => occurrence.Date <= startDay && startDay < occurrence.PeriodEndExclusive))
				{
					var reflectiveExists = await context.Set<Reflective>()
						.AnyAsync(item => item.PolarisCycleId == cycle.Id && item.DecreeId == decree.Id, cancellationToken);
					if (!reflectiveExists)
					{
						context.Add(new Reflective
						{
							Description = decree.Title,
							PolarisCycleId = cycle.Id,
							DecreeId = decree.Id,
							Executed = false,
						});
						created++;
					}
				}

				continue;
			}

			foreach (var occurrence in await orbitService.SeekOccurrencesAsync(decree, decree.Orbit!, decreeSeekThroughExclusive, cancellationToken))
			{
				var attentiveExists = await context.Attentives.AnyAsync(
					item => item.DecreeId == decree.Id
						&& item.Date == occurrence.Date
						&& item.Time == occurrence.StartTime
						&& item.PolarisCycleId == null,
					cancellationToken);
				if (attentiveExists)
				{
					continue;
				}

				var attentive = new Attentive
				{
					DecreeId = decree.Id,
					Date = occurrence.Date,
					Time = occurrence.StartTime,
					PeriodEndDate = occurrence.PeriodEndExclusive > occurrence.Date.AddDays(1)
						? occurrence.PeriodEndExclusive
						: null,
					Estimation = decree.DefaultLength,
				};
				attentive.Normalize();
				context.Attentives.Add(attentive);
				created++;
			}
		}

		var dueObjectives = await context.Objectives
			.AsNoTracking()
			.IgnoreAutoIncludes()
			.Where(objective => objective.Due != null
				&& candidateDates.Contains(objective.Due.Value)
				&& objective.Status != ObjectiveStatus.Done
				&& objective.Status != ObjectiveStatus.Archived
				&& objective.Status != ObjectiveStatus.Failed)
			.ToListAsync(cancellationToken);

		foreach (var objective in dueObjectives)
		{
			var exists = await context.Eventives.AnyAsync(item => item.ObjectiveId == objective.Id && item.RecurrenceDate == objective.Due!.Value, cancellationToken);
			if (exists)
			{
				continue;
			}

			context.Eventives.Add(new Eventive
			{
				ObjectiveId = objective.Id,
				Date = objective.Due!.Value,
				RecurrenceDate = objective.Due!.Value,
			});
			created++;
		}

		// Advanced orbit states persist even when no new instances were created.
		await context.SaveChangesAsync(cancellationToken);
		return created;
	}

	/// <summary>
	/// Ensures a dated fate's eventive exists for an occurrence day, returning whether one was created.
	/// </summary>
	private Task<bool> EnsureFateEventiveAsync(Fate fate, DateOnly day, CancellationToken cancellationToken)
	{
		return EnsureFateEventiveCoreAsync(fate, day, fate.StartTime, fate.EndTime, fate.ResolveEventiveDuration(), cancellationToken);
	}

	/// <summary>
	/// Ensures a fate's eventive exists for an orbit occurrence, returning whether one was created. Span
	/// occurrences carry their own start/end/length; granular occurrences fall back to the fate's time spec.
	/// </summary>
	private Task<bool> EnsureFateEventiveAsync(Fate fate, OrbitOccurrenceInstance occurrence, CancellationToken cancellationToken)
	{
		return EnsureFateEventiveCoreAsync(
			fate,
			occurrence.Date,
			occurrence.StartTime ?? fate.StartTime,
			occurrence.EndTime ?? (occurrence.StartTime is null ? fate.EndTime : null),
			occurrence.DurationMinutes ?? fate.ResolveEventiveDuration(),
			cancellationToken);
	}

	private async Task<bool> EnsureFateEventiveCoreAsync(Fate fate, DateOnly day, TimeOnly? startTime, TimeOnly? endTime, int? estimation, CancellationToken cancellationToken)
	{
		// PEP101: a locked whole-fate pauses orbit generation; a locked single occurrence blocks just itself.
		if (await dependencyGate.IsFateMaterializationBlockedAsync(fate.Id, day, startTime, cancellationToken))
		{
			return false;
		}

		var exists = await context.Eventives.AnyAsync(
			item => item.FateId == fate.Id && item.RecurrenceDate == day && item.RecurrenceTime == startTime,
			cancellationToken);
		if (exists)
		{
			return false;
		}

		var eventive = new Eventive
		{
			FateId = fate.Id,
			Date = day,
			StartTime = startTime,
			EndTime = endTime,
			RecurrenceDate = day,
			RecurrenceTime = startTime,
			Estimation = estimation,
		};
		eventive.Normalize();
		context.Eventives.Add(eventive);
		return true;
	}
}
