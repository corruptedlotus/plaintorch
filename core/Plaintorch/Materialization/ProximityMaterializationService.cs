using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pleiades.Orbits;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Preferences;
using Pleiades.Plaintorch.State;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Materialization;

/// <summary>
/// Hardens declarative occurrences into persisted rows on the two non-interactive triggers (Strategy 1 / soft
/// agenda, PEP100). <see cref="MaterializeForCycleAsync"/> runs at Polaris cycle begin and generates the
/// cycle-bound reflectives of matching lunar reflect-decrees. <see cref="MaterializeForNowAsync"/> is the
/// rolling harden-on-time pass: when time is treated as an interaction it advances each schedule's cursor to
/// <c>now</c> and hardens the occurrences whose time has arrived, so history becomes durable rows while every
/// still-future occurrence stays a live projection (see <see cref="AgendaProjectionService"/>).
/// </summary>
/// <remarks>
/// An occurrence's RECURRENCE-ID (owner id + original date/time) is its identity, so an occurrence already
/// hardened by interaction is recognized rather than duplicated, and the pass is idempotent. When
/// <see cref="MaterializationPolicyOptions.TimeIsInteraction"/> is <see langword="false"/> the rolling pass
/// hardens nothing — past occurrences stay projections and recompute when their schedule changes.
/// </remarks>
public sealed class ProximityMaterializationService(
	PlainfraContext context,
	PlaintorchOrbitService orbitService,
	DependencyGateService dependencyGate,
	TimeframeAffinityResolver affinityResolver,
	MaterializationPolicyOptions policy,
	IOptionsSnapshot<AgendaPreferences> agendaPreferences,
	VaultAuditLogService auditLogService)
{
	/// <summary>
	/// Generates the cycle-bound reflectives for a beginning cycle from its matching lunar reflect-decrees. The
	/// cycle's other inclusions are projected on read, so nothing else is hardened here. Returns the number of
	/// reflectives created.
	/// </summary>
	public async Task<int> MaterializeForCycleAsync(PolarisCycle cycle, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(cycle);
		if (cycle.StartTime is null)
		{
			return 0;
		}

		var startDay = DateOnly.FromDateTime(cycle.StartTime.Value.LocalDateTime);
		var created = 0;

		// Lunar reflect-decrees resolve at day granularity against the cycle's start day and bind their
		// reflective to the cycle. Seeking advances the schedule state so the same occurrence is not re-emitted;
		// the rolling harden-on-time pass deliberately leaves reflect-decrees alone, so cycle begin is the only
		// place their cursor advances.
		var reflectDecrees = await context.Decrees
			.IgnoreAutoIncludes()
			.Where(decree => decree.Status == DecreeStatus.Active && decree.Orbit != null && decree.Reflect)
			.ToListAsync(cancellationToken);

		foreach (var decree in reflectDecrees)
		{
			if (!await orbitService.IsInLunarHierarchyAsync(decree.DirectiveId, cancellationToken))
			{
				continue;
			}

			var occurrences = await orbitService.SeekOccurrencesAsync(decree, decree.Orbit!, startDay.AddDays(1), cancellationToken);
			if (!occurrences.Any(occurrence => occurrence.Date <= startDay && startDay < occurrence.PeriodEndExclusive))
			{
				continue;
			}

			var reflectiveExists = await context.Set<Reflective>()
				.AnyAsync(item => item.PolarisCycleId == cycle.Id && item.DecreeId == decree.Id, cancellationToken);
			if (reflectiveExists)
			{
				continue;
			}

			// Auto-inclusion: a cycle-bound reflective takes the same combined affinity an executive gets — the decree's
			// nearest directive availability, else its college (PEP100 patch 2).
			context.Add(new Reflective
			{
				Description = decree.Title,
				PolarisCycleId = cycle.Id,
				DecreeId = decree.Id,
				Executed = false,
				AffinityTimeframeId = await affinityResolver.ResolveAsync(decree.DirectiveId, decree.College, cancellationToken),
			});
			created++;
		}

		await context.SaveChangesAsync(cancellationToken);
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
	/// Harden-on-time: hardens every occurrence whose time has arrived at or before <paramref name="now"/>.
	/// Orbit fates and decrees are seeked through the instant (their cursor advances to <paramref name="now"/>),
	/// dated fates and due objectives harden once their occurrence is reached. Does nothing when time is not
	/// treated as an interaction. Returns the number of rows hardened.
	/// </summary>
	public async Task<int> MaterializeForNowAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
	{
		if (!policy.TimeIsInteraction)
		{
			return 0;
		}

		var localNow = now.LocalDateTime;
		var today = DateOnly.FromDateTime(localNow);
		var created = 0;

		// Orbit fates: seek through now (instant), hardening occurrences whose time has already arrived; the
		// still-future occurrences of today are left for a later tick. Active fates harden as Pending; an opted-out
		// fate keeps generating but its occurrences only harden — stamped OptOut so they stay hidden — when the
		// user's auto-materialise-optout preference is enabled, so by default they remain projections (PEP111/PEP116).
		var autoMaterialiseOptOut = agendaPreferences.Value.AutoMaterialiseOptOut;
		var orbitFates = await context.Fates
			.IgnoreAutoIncludes()
			.Where(fate => fate.Orbit != null
				&& (fate.Status == FateStatus.Active
					|| (fate.Status == FateStatus.OptOut && autoMaterialiseOptOut)))
			.ToListAsync(cancellationToken);
		foreach (var fate in orbitFates)
		{
			var resolution = fate.Status == FateStatus.OptOut ? EventiveResolution.OptOut : EventiveResolution.Pending;
			foreach (var occurrence in await orbitService.SeekOccurrencesThroughInstantAsync(fate, fate.Orbit!, localNow, cancellationToken))
			{
				created += await EnsureFateEventiveAsync(fate, occurrence, resolution, cancellationToken) ? 1 : 0;
			}

			// Advance the denormalized next occurrence as time passes; only touch the row when it actually rolls
			// forward, so an idle tick writes nothing.
			var next = await orbitService.ComputeNextOccurrenceAsync(fate, localNow, cancellationToken);
			if (fate.NextOccurrence != next)
			{
				fate.NextOccurrence = next;
			}
		}

		// Orbit decrees (non-reflect): seek through now, hardening crossed attentives. Reflect-decrees are
		// cycle-bound; their schedule state is not advanced here.
		var orbitDecrees = await context.Decrees
			.IgnoreAutoIncludes()
			.Where(decree => decree.Status == DecreeStatus.Active && decree.Orbit != null)
			.ToListAsync(cancellationToken);
		foreach (var decree in orbitDecrees)
		{
			if (decree.Reflect && await orbitService.IsInLunarHierarchyAsync(decree.DirectiveId, cancellationToken))
			{
				continue;
			}

			foreach (var occurrence in await orbitService.SeekOccurrencesThroughInstantAsync(decree, decree.Orbit!, localNow, cancellationToken))
			{
				created += await EnsureDecreeAttentiveAsync(decree, occurrence, cancellationToken) ? 1 : 0;
			}

			var next = await orbitService.ComputeNextOccurrenceAsync(decree, localNow, cancellationToken);
			if (decree.NextOccurrence != next)
			{
				decree.NextOccurrence = next;
			}
		}

		// Due objectives whose due moment has arrived. An objective has a single due occurrence, so the presence of
		// any eventive for it means it is already hardened.
		var tomorrowStart = today.AddDays(1).ToDateTime(TimeOnly.MinValue);
		var dueObjectives = await context.Objectives
			.AsNoTracking()
			.IgnoreAutoIncludes()
			.Where(objective => objective.Due != null && objective.Due.Moment < tomorrowStart
				&& objective.Status != ObjectiveStatus.Done
				&& objective.Status != ObjectiveStatus.Archived
				&& objective.Status != ObjectiveStatus.Failed
				&& !context.Eventives.Any(eventive => eventive.ObjectiveId == objective.Id))
			.ToListAsync(cancellationToken);
		foreach (var objective in dueObjectives)
		{
			context.Eventives.Add(new Eventive
			{
				ObjectiveId = objective.Id,
				Epoch = Epoch.From(objective.Due!.Date, timeOfDay: null, OrbitUnit.Day),
				RecurrenceId = objective.Due!.Date.ToDateTime(TimeOnly.MinValue),
			});
			created++;
		}

		// The advanced orbit cursors persist even when no new rows were created.
		await context.SaveChangesAsync(cancellationToken);
		if (created > 0)
		{
			await auditLogService.WriteAsync(
				"daemon",
				"rolling.materialize",
				subjectType: nameof(PolarisCycle),
				details: new { created, from = localNow.ToString("yyyy-MM-dd HH:mm") },
				cancellationToken: cancellationToken);
		}

		return created;
	}

	/// <summary>
	/// Hardens an orbit decree's unbound attentive for an occurrence, returning whether one was created. The
	/// RECURRENCE-ID (decree + original slot) is the identity, so an already-hardened occurrence — including one
	/// that was rescheduled — is recognized rather than duplicated.
	/// </summary>
	private async Task<bool> EnsureDecreeAttentiveAsync(Decree decree, OrbitOccurrenceInstance occurrence, CancellationToken cancellationToken)
	{
		var slot = occurrence.Moment;
		var exists = await context.Attentives.AnyAsync(
			item => item.DecreeId == decree.Id && item.RecurrenceId == slot,
			cancellationToken);
		if (exists)
		{
			return false;
		}

		var attentive = new Attentive
		{
			DecreeId = decree.Id,
			Epoch = Epoch.For(occurrence),
			RecurrenceId = slot,
		};
		context.Attentives.Add(attentive);
		return true;
	}

	/// <summary>
	/// Ensures a fate's eventive exists for an orbit occurrence, returning whether one was created. A span
	/// occurrence carries its span and a super-day one its period as the epoch duration; any other fills its
	/// granularity window (null duration).
	/// </summary>
	private async Task<bool> EnsureFateEventiveAsync(Fate fate, OrbitOccurrenceInstance occurrence, EventiveResolution resolution, CancellationToken cancellationToken)
	{
		var slot = occurrence.Moment;

		// PEP101: a locked whole-fate pauses orbit generation; a locked single occurrence blocks just itself.
		if (await dependencyGate.IsFateMaterializationBlockedAsync(fate.Id, slot, cancellationToken))
		{
			return false;
		}

		var exists = await context.Eventives.AnyAsync(
			item => item.FateId == fate.Id && item.RecurrenceId == slot,
			cancellationToken);
		if (exists)
		{
			return false;
		}

		var eventive = new Eventive
		{
			FateId = fate.Id,
			Epoch = Epoch.For(occurrence),
			RecurrenceId = slot,
			Resolution = resolution,
		};
		context.Eventives.Add(eventive);
		return true;
	}
}
