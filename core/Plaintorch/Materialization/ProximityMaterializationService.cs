using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pleiades.Orbits;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Plaintorch.Preferences;
using Pleiades.Plaintorch.State;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Materialization;

/// <summary>
/// Hardens declarative occurrences into persisted rows on the two non-interactive triggers (Strategy 1 / soft
/// agenda, PEP100). <see cref="MaterializeForCycleAsync"/> runs at Polaris cycle begin and generates the
/// cycle-bound reflectives of lunar reflect-decrees (PEP104). <see cref="MaterializeForNowAsync"/> is the
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
	TerminativeService terminativeService,
	MaterializationPolicyOptions policy,
	IOptionsSnapshot<AgendaPreferences> agendaPreferences,
	IOptionsSnapshot<ReflectionPreferences> reflectionPreferences,
	VaultAuditLogService auditLogService)
{
	/// <summary>
	/// Generates the cycle-bound reflectives for a beginning cycle (PEP104). Every live reflect-decree whose orbit
	/// resolves onto the cycle's start day is taken, and <see cref="ReflectionPreferences.BaseDailyReflectives"/> more
	/// are drawn at random from the live reflect-decrees that have no orbit. A reflect-decree is live when it is active
	/// and its lunar hierarchy is active (<see cref="PlaintorchOrbitService.IsLunarHierarchyActiveAsync"/>). The cycle's
	/// other inclusions are projected on read, so nothing else is hardened here. Returns the number of reflectives
	/// created.
	/// </summary>
	/// <remarks>
	/// A decree gets at most one reflective per cycle, so running this twice for the same cycle creates nothing new:
	/// the orbit matches are already there, and pool decrees already reflected in the cycle count toward the draw. A
	/// reflect-decree whose stored orbit no longer resolves is logged and left out
	/// (<see cref="PlaintorchOrbitService.IsolateAsync"/>), so a begin, whose start is committed by now, still completes.
	/// </remarks>
	public async Task<int> MaterializeForCycleAsync(PolarisCycle cycle, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(cycle);
		if (cycle.StartTime is null)
		{
			return 0;
		}

		var startDay = DateOnly.FromDateTime(cycle.StartTime.Value.LocalDateTime);
		var activeDecrees = TemporalActivation.Statuses<DecreeStatus>();
		var reflectDecrees = await context.Decrees
			.IgnoreAutoIncludes()
			.Where(decree => activeDecrees.Contains(decree.Status) && decree.Reflect)
			.ToListAsync(cancellationToken);
		var reflectedDecreeIds = (await context.Set<Reflective>()
				.Where(item => item.PolarisCycleId == cycle.Id && item.DecreeId != null)
				.Select(item => item.DecreeId!)
				.ToListAsync(cancellationToken))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		var selected = new List<Decree>();
		var pool = new List<Decree>();
		foreach (var decree in reflectDecrees)
		{
			if (!await orbitService.IsLunarHierarchyActiveAsync(decree.DirectiveId, cancellationToken))
			{
				continue;
			}

			if (string.IsNullOrWhiteSpace(decree.Orbit))
			{
				pool.Add(decree);
				continue;
			}

			// Orbit reflect-decrees resolve at day granularity against the cycle's start day. Seeking advances the
			// schedule state so the same occurrence is not re-emitted; the rolling harden-on-time pass deliberately
			// leaves reflect-decrees alone, so cycle begin is the only place their cursor advances. A paused decree is
			// skipped above without seeking, and the days it missed are never generated afterwards. A decree whose orbit
			// faults is logged and left out, so the begin, already committed, still reflects the others.
			await orbitService.IsolateAsync(decree, "cycle-begin reflection", async () =>
			{
				var occurrences = await orbitService.SeekOccurrencesAsync(decree, decree.Orbit, startDay.AddDays(1), cancellationToken);
				if (occurrences.Any(occurrence => occurrence.Date <= startDay && startDay < occurrence.PeriodEndExclusive))
				{
					selected.Add(decree);
				}
			});
		}

		var drawCount = Math.Max(0, reflectionPreferences.Value.BaseDailyReflectives)
			- pool.Count(decree => reflectedDecreeIds.Contains(decree.Id));
		if (drawCount > 0)
		{
			var candidates = pool.Where(decree => !reflectedDecreeIds.Contains(decree.Id)).ToArray();
			Random.Shared.Shuffle(candidates);
			selected.AddRange(candidates.Take(drawCount));
		}

		var created = 0;
		foreach (var decree in selected)
		{
			if (!reflectedDecreeIds.Add(decree.Id))
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
	/// Orbit fates and decrees are seeked through the instant (their cursor advances to <paramref name="now"/>), and
	/// every due that acts hardens its terminative once its day is reached (PEP103 patch 7). Does nothing when time is not
	/// treated as an interaction. Returns the number of rows hardened.
	/// </summary>
	/// <remarks>
	/// Each fate and decree is resolved in isolation (<see cref="PlaintorchOrbitService.IsolateAsync"/>): one whose stored
	/// orbit no longer parses or builds is logged, hardens nothing and caches no next occurrence, and the pass hardens every
	/// other owner (PEP103 I-124).
	/// </remarks>
	public async Task<int> MaterializeForNowAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
	{
		if (!policy.TimeIsInteraction)
		{
			return 0;
		}

		var localNow = now.LocalDateTime;
		var today = DateOnly.FromDateTime(localNow);
		var created = 0;

		// Orbit fates whose temporal activation is on (PEP103 patch 6): seek through now (instant), hardening occurrences
		// whose time has already arrived; the still-future occurrences of today are left for a later tick. Active fates
		// harden as Pending. An opted-out fate is under way too, but its occurrences only harden — stamped OptOut so they
		// stay hidden — when the user's auto-materialise-optout preference is enabled; by default they remain projections
		// and its cursor waits (PEP111/PEP116), so turning the preference on hardens what has elapsed. Its next occurrence
		// still rolls forward, and taking it back to Active seeks its cursor to now (StructureReactions), hardening none of
		// the occurrences it passed while opted out.
		var autoMaterialiseOptOut = agendaPreferences.Value.AutoMaterialiseOptOut;
		var activeFates = TemporalActivation.Statuses<FateStatus>();
		var orbitFates = await context.Fates
			.IgnoreAutoIncludes()
			.Where(fate => fate.Orbit != null && activeFates.Contains(fate.Status))
			.ToListAsync(cancellationToken);
		// Each owner's orbit work is isolated (PEP103 I-124): one whose orbit no longer resolves is logged and skipped, its
		// cursor left where it was and its cached next occurrence cleared, and every other owner is still hardened.
		foreach (var fate in orbitFates)
		{
			var resolved = await orbitService.IsolateAsync(fate, "the rolling harden-on-time pass", async () =>
			{
				var optedOut = fate.Status == FateStatus.OptOut;
				if (!optedOut || autoMaterialiseOptOut)
				{
					var resolution = optedOut ? EventiveResolution.OptOut : EventiveResolution.Pending;
					foreach (var occurrence in await orbitService.SeekOccurrencesThroughInstantAsync(fate, fate.Orbit!, now, cancellationToken))
					{
						created += await EnsureFateEventiveAsync(fate, occurrence, resolution, cancellationToken) ? 1 : 0;
					}
				}

				// Advance the denormalized next occurrence as time passes; only touch the row when it actually rolls
				// forward, so an idle tick writes nothing.
				var next = await orbitService.ComputeNextOccurrenceAsync(fate, now, cancellationToken);
				if (fate.NextOccurrence != next)
				{
					fate.NextOccurrence = next;
				}
			});
			if (!resolved && fate.NextOccurrence is not null)
			{
				fate.NextOccurrence = null;
			}
		}

		// Orbit decrees (non-reflect): seek through now, hardening crossed attentives. Reflect-decrees are
		// cycle-bound; their schedule state is not advanced here.
		var activeDecrees = TemporalActivation.Statuses<DecreeStatus>();
		var orbitDecrees = await context.Decrees
			.IgnoreAutoIncludes()
			.Where(decree => activeDecrees.Contains(decree.Status) && decree.Orbit != null)
			.ToListAsync(cancellationToken);
		foreach (var decree in orbitDecrees)
		{
			if (decree.Reflect && await orbitService.IsInLunarHierarchyAsync(decree.DirectiveId, cancellationToken))
			{
				continue;
			}

			var resolved = await orbitService.IsolateAsync(decree, "the rolling harden-on-time pass", async () =>
			{
				foreach (var occurrence in await orbitService.SeekOccurrencesThroughInstantAsync(decree, decree.Orbit!, now, cancellationToken))
				{
					created += await EnsureDecreeAttentiveAsync(decree, occurrence, cancellationToken) ? 1 : 0;
				}

				var next = await orbitService.ComputeNextOccurrenceAsync(decree, now, cancellationToken);
				if (decree.NextOccurrence != next)
				{
					decree.NextOccurrence = next;
				}
			});
			if (!resolved && decree.NextOccurrence is not null)
			{
				decree.NextOccurrence = null;
			}
		}

		// Every due that acts, on or before today, hardens its terminative (PEP103 patch 7): an item finished or set aside
		// hardens none, and a checkpoint keeps acting past its due until it unlocks. An item has one terminative, so one that
		// already has it is skipped; the state engine keeps it on the due and resolved from then on.
		var tomorrowStart = today.AddDays(1).ToDateTime(TimeOnly.MinValue);
		var acting = await terminativeService.FindActingAsync(context, fromInclusive: null, tomorrowStart, tracking: false, cancellationToken);
		var hardened = (await terminativeService.FindOfAsync(context, acting, cancellationToken))
			.Select(item => item.RecurrenceOwnerUid)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (var origin in acting.Where(origin => !hardened.Contains(origin.Id)))
		{
			context.Terminatives.Add(terminativeService.Spawn(origin));
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
			Epoch = PlaintorchOrbitService.EpochOf(decree, occurrence),
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
			Epoch = PlaintorchOrbitService.EpochOf(fate, occurrence),
			RecurrenceId = slot,
			Resolution = resolution,
		};
		context.Eventives.Add(eventive);
		return true;
	}
}
