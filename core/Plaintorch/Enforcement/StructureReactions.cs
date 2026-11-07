using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orbits;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Plaintorch.Materialization;
using Pleiades.Plaintorch.State;
using Pleiades.Puck;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Enforcement;

/// <summary>
/// The engine's first phase (PEP103): what a requested change implies for the backlog's structure, whatever the lifecycle
/// rules go on to say. It runs before the save is judged, so the judge sees the edges, cursors and memberships the change
/// leaves behind.
/// </summary>
/// <remarks>
/// In order:
/// <list type="number">
/// <item><description>Deleting an incentive keeps its work in ended cycles and removes the rest (SH-01, SH-02).</description></item>
/// <item><description>Deleting a PUCK-named entity ends its sync boundary (SH-03).</description></item>
/// <item><description>A directive's availability never outlives its timeframe's role (SH-04).</description></item>
/// <item><description>A changed orbit resets its schedule cursor, and resuming a set-aside fate or decree seeks its cursor to
/// now (SH-05, SH-06, DE-08).</description></item>
/// <item><description>A dependency added hardens the occurrences it names (SH-17).</description></item>
/// <item><description>Deleting an endpoint releases the stored edges naming it (SH-18, DP-12).</description></item>
/// <item><description>A structural change purges the stored edges the structure now contradicts or restates (SH-19).</description></item>
/// </list>
/// The engine's own moves on joining a sprint or a cycle follow these, in <see cref="MembershipReactions"/>. Context-bound
/// services are resolved from the scope when needed, for the reason <see cref="StateEngine"/> gives.
/// </remarks>
public sealed class StructureReactions(EntityLifecycleResolver lifecycleResolver, IServiceProvider serviceProvider)
{
	/// <summary>
	/// Applies every structural reaction to the save.
	/// </summary>
	public async Task ApplyAsync(StatePass pass, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(pass);
		var context = pass.Context;
		await ReleaseDeletedIncentiveWorkAsync(context, cancellationToken);
		await EndDeletedBoundariesAsync(context, cancellationToken);
		await ClearStaleAvailabilityReferencesAsync(context, cancellationToken);
		await ResetChangedScheduleCursorsAsync(context, cancellationToken);
		await SeekResumedCursorsAsync(context, cancellationToken);
		await HardenReferencedOccurrencesAsync(context, cancellationToken);
		await ReleaseDeletedEndpointDependenciesAsync(context, cancellationToken);
		await PurgeContradictedDependenciesAsync(context, cancellationToken);
	}

	/// <summary>
	/// Deleting an incentive keeps the work already recorded against it. In the same unit of work that deletes an
	/// objective or a decree (a fate is never worked), each of its work records in an <em>ended</em> Polaris cycle — an
	/// executive, or a decree's reflective — is kept with its reference cleared, so the cycle's record of the work
	/// (executed, elapsed, allocations) survives the backlog item; each in the active cycle or a planned/forecast one is
	/// removed with it, since that plan can no longer be worked. Riding the save covers every pathway: the API deletes and
	/// the markdown watcher's delete-from-database action, whose delete-blocker check therefore does not count these records
	/// (<see cref="VaultEntityGateway.FindDeleteBlockersAsync"/>).
	/// </summary>
	private static async Task ReleaseDeletedIncentiveWorkAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var deletedIds = context.ChangeTracker.Entries<Incentive>()
			.Where(entry => entry.State == EntityState.Deleted)
			.Select(entry => entry.Entity.Id)
			.ToList();
		if (deletedIds.Count == 0)
		{
			return;
		}

		var executives = await context.Set<Executive>()
			.Include(executive => executive.PolarisCycle)
			.Where(executive => executive.IncentiveId != null && deletedIds.Contains(executive.IncentiveId))
			.ToListAsync(cancellationToken);
		foreach (var executive in executives)
		{
			if (executive.PolarisCycle?.EndTime is not null)
			{
				executive.Incentive = null;
				executive.IncentiveId = null;
			}
			else
			{
				context.Remove(executive);
			}
		}

		var reflectives = await context.Set<Reflective>()
			.Include(reflective => reflective.PolarisCycle)
			.Where(reflective => reflective.DecreeId != null && deletedIds.Contains(reflective.DecreeId))
			.ToListAsync(cancellationToken);
		foreach (var reflective in reflectives)
		{
			if (reflective.PolarisCycle?.EndTime is not null)
			{
				reflective.Decree = null;
				reflective.DecreeId = null;
			}
			else
			{
				context.Remove(reflective);
			}
		}
	}

	/// <summary>
	/// A deleted entity's synchronization boundary ends with it, in the same save and whatever the pathway (an API delete,
	/// or the watcher's delete after the note went): the boundary is the entity's own state, never outliving it.
	/// </summary>
	private static Task EndDeletedBoundariesAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var deleted = context.ChangeTracker.Entries()
			.Where(entry => entry.State == EntityState.Deleted && entry.Entity is IPuckNamedEntity)
			.Select(entry => (IPuckNamedEntity)entry.Entity)
			.ToList();
		return deleted.Count == 0
			? Task.CompletedTask
			: VaultImplicitBoundaryService.StageBoundaryEndsAsync(context, deleted, cancellationToken);
	}

	/// <summary>
	/// Deep-interception availability hygiene (PEP100 patch 2, D9): a directive availability never outlives its
	/// timeframe's role. In the same unit of work that deletes a timeframe, switches one away from
	/// <see cref="TimeframeInclusion.Availability"/>, or deletes a lunar directive (whose timeframes the database cascade
	/// removes out of EF's sight, so their ids are read from the store), every directive pointing at one of those
	/// timeframes is loaded tracked and cleared. Riding the save covers any save that deletes a lunar directive or touches a
	/// timeframe, whatever its pathway. Note that the watcher does not reach that action for a deleted lunar note today:
	/// discovery cannot recover a Quiet/Freeform note's identity once the file is gone and ignores the removal (an open gap
	/// recorded in <c>core/.DISCUSSION.md</c>); once discovery produces the action, the engine covers it unchanged. The tracked
	/// clear is what the change feed announces; the SetNull foreign key stays only as the database-level backstop.
	/// </summary>
	private static async Task ClearStaleAvailabilityReferencesAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var staleIds = new HashSet<long>();
		foreach (var entry in context.ChangeTracker.Entries<Timeframe>())
		{
			var leftAvailability = entry.State == EntityState.Modified
				&& entry.Property(nameof(Timeframe.AutoInclusion)).IsModified
				&& entry.OriginalValues.GetValue<TimeframeInclusion>(nameof(Timeframe.AutoInclusion)) == TimeframeInclusion.Availability
				&& entry.Entity.AutoInclusion != TimeframeInclusion.Availability;
			if (entry.State == EntityState.Deleted || leftAvailability)
			{
				staleIds.Add(entry.Entity.Id);
			}
		}

		var deletedLunarIds = context.ChangeTracker.Entries<Directive>()
			.Where(entry => entry.State == EntityState.Deleted && entry.Entity is LunarDirective)
			.Select(entry => entry.Entity.Id)
			.ToList();
		if (deletedLunarIds.Count > 0)
		{
			staleIds.UnionWith(await context.Timeframes
				.AsNoTracking()
				.Where(timeframe => deletedLunarIds.Contains(timeframe.DirectiveId))
				.Select(timeframe => timeframe.Id)
				.ToListAsync(cancellationToken));
		}

		if (staleIds.Count == 0)
		{
			return;
		}

		// Loading brings every stored reference into the tracker; the clear then runs over the tracker, so a directive
		// whose availability this same save changes is judged by its pending value rather than the stored one.
		var staleKeys = staleIds.Select(id => (long?)id).ToList();
		await context.Directives
			.Where(directive => staleKeys.Contains(directive.AvailabilityTimeframeId))
			.LoadAsync(cancellationToken);
		foreach (var entry in context.ChangeTracker.Entries<Directive>().ToList())
		{
			if (entry.State != EntityState.Deleted
				&& entry.Entity.AvailabilityTimeframeId is { } timeframeId
				&& staleIds.Contains(timeframeId))
			{
				entry.Entity.AvailabilityTimeframeId = null;
			}
		}
	}

	/// <summary>
	/// Deep-interception schedule-change rule (Strategy 1): when a fate or decree is created with, or changed to, a
	/// different orbit, its seek cursor is reset to a fresh state anchored today. Riding the save covers every write
	/// pathway, so editing an orbit in frontmatter resets the cursor exactly as an API edit does, and the projection
	/// recomputes on the new orbit while already-hardened occurrences persist untouched. Timeframes created with, or changed
	/// to, a different orbit get the same treatment for their orbit state (PEP100 patch 2), except that a recurring
	/// timeframe orbit anchors at the earlier of today and the open Polaris cycle's day, and a timeframe's fixed <c>Z{…}</c>
	/// literal is read on the timeframe calendar (the vault default), as a declarative's is read on its own resolved
	/// calendar.
	/// </summary>
	private async Task ResetChangedScheduleCursorsAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var toReset = new List<(Incentive Incentive, string? Orbit)>();
		foreach (var entry in context.ChangeTracker.Entries<Fate>())
		{
			if (OrbitCursorNeedsReset(entry, entry.Entity.Orbit))
			{
				toReset.Add((entry.Entity, entry.Entity.Orbit));
			}
		}

		foreach (var entry in context.ChangeTracker.Entries<Decree>())
		{
			if (OrbitCursorNeedsReset(entry, entry.Entity.Orbit))
			{
				toReset.Add((entry.Entity, entry.Entity.Orbit));
			}
		}

		// Timeframe orbits (PEP100 patch 2) follow the declarative reset policy: set or changed re-anchors, cleared
		// removes the state. A calendar change never resets them, as it never resets a declarative's.
		var timeframesToReset = context.ChangeTracker.Entries<Timeframe>()
			.Where(entry => OrbitCursorNeedsReset(entry, entry.Entity.Orbit))
			.Select(entry => entry.Entity)
			.ToList();

		if (toReset.Count == 0 && timeframesToReset.Count == 0)
		{
			return;
		}

		var orbitService = serviceProvider.GetRequiredService<PlaintorchOrbitService>();
		var today = DateOnly.FromDateTime(DateTime.Today);
		foreach (var (incentive, orbit) in toReset)
		{
			// A one-off fixed-datetime orbit (a Z{…} literal) anchors its cursor at the literal's own date, so its
			// single occurrence is caught by a seek whether it is in the past or the future (PEP111); a recurring
			// schedule anchors today so it generates forward with no backfill. The literal names a day on the calendar
			// its declarative resolves on — Gregorian for a pinned one-off, but a one-off may be moved onto another.
			await orbitService.ResetStateAsync(incentive, orbit, ResetEpoch(orbit, today, orbitService.ResolveCalendar(incentive)), cancellationToken);
		}

		if (timeframesToReset.Count == 0)
		{
			return;
		}

		// A timeframe anchors no later than the open cycle's day, or an orbit set after midnight could never select the
		// cycle begun the evening before (an orbit yields nothing before its epoch). Unlike a declarative, a timeframe
		// state is only ever previewed and never hardens occurrences, so anchoring back costs no backfill.
		var activeCycleDay = await ResolveActiveCycleDayAsync(context, cancellationToken);
		var timeframeAnchor = activeCycleDay is { } cycleDay && cycleDay < today ? cycleDay : today;
		// A timeframe's Z{…} literal names a day on the calendar its orbit is read on, not a Gregorian one.
		var timeframeCalendar = orbitService.ResolveDefaultCalendar();
		foreach (var timeframe in timeframesToReset)
		{
			await orbitService.ResetTimeframeStateAsync(
				timeframe,
				timeframe.Orbit,
				ResetEpoch(timeframe.Orbit, timeframeAnchor, timeframeCalendar),
				cancellationToken);
		}
	}

	/// <summary>
	/// Resuming a fate or decree whose temporal activation was off — out of a deferring status, or a fate out of Passed (PEP103
	/// I-191), back to a running one — seeks its cursor to now, read on its zone's clock, so generation picks up from now with
	/// no backfill of the occurrences that elapsed while it was switched off (PEP100, PEP111). A Passed fate the engine brings
	/// back seeks the same way, in <see cref="FateReactions"/>. A fate leaving OptOut seeks the same way: an opted-out fate is under way (PEP103
	/// patch 6), but time hardens none of its occurrences unless the auto-materialise-optout preference says so, and those it
	/// passed while opted out stay unhardened once it is taken back. Runs after the cursor resets, so a resume that also
	/// changes the orbit seeks the fresh cursor; riding the save, a frontmatter <c>status:</c> edit resumes the same way as
	/// the API.
	/// </summary>
	private async Task SeekResumedCursorsAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var resumed = new List<(Incentive Incentive, string Orbit)>();
		foreach (var entry in context.ChangeTracker.Entries<Incentive>())
		{
			var scheduled = entry.Entity switch
			{
				Fate fate => fate.Orbit,
				Decree decree => decree.Orbit,
				_ => null,
			};
			if (entry.State != EntityState.Modified
				|| string.IsNullOrWhiteSpace(scheduled)
				|| lifecycleResolver.StatusPropertyOf(entry.Entity.GetType()) is not { } statusProperty)
			{
				continue;
			}

			var status = entry.Property(statusProperty.Name);
			if (status.IsModified
				&& status.OriginalValue is Enum previous
				&& TemporalActivation.IsActive(lifecycleResolver.StateOf((Enum)status.CurrentValue!))
				&& (!TemporalActivation.IsActive(lifecycleResolver.StateOf(previous))
					|| (previous is FateStatus.OptOut && status.CurrentValue is not FateStatus.OptOut)))
			{
				resumed.Add((entry.Entity, scheduled));
			}
		}

		if (resumed.Count == 0)
		{
			return;
		}

		var orbitService = serviceProvider.GetRequiredService<PlaintorchOrbitService>();
		var now = DateTimeOffset.Now;
		foreach (var (incentive, orbit) in resumed)
		{
			await orbitService.FastForwardToNowAsync(incentive, orbit, now, cancellationToken);
		}
	}

	/// <summary>
	/// The epoch a reset schedule cursor anchors at: a lone fixed-datetime literal's own date with its year, month and
	/// day read on <paramref name="literalCalendar"/> (a declarative's resolved calendar, the vault default calendar for a
	/// timeframe), otherwise <paramref name="anchor"/> — today for a declarative, the earlier of today and the open
	/// cycle's day for a timeframe. A literal naming a day the calendar cannot hold falls back to
	/// <paramref name="anchor"/> as well, so an out-of-range literal never fails the save.
	/// </summary>
	private static DateOnly ResetEpoch(string? orbit, DateOnly anchor, IOrbitCalendar literalCalendar)
		=> (orbit is not null ? OrbitDays.FixedLiteralDate(orbit, literalCalendar) : null) ?? anchor;

	/// <summary>
	/// Resolves the candidate day (<see cref="TimeframeCandidateService.CycleDay"/>) of the strictly active Polaris cycle
	/// — begun and not ended — as this unit of work leaves it, or <see langword="null"/> when no cycle is open. A tracked
	/// cycle's pending state wins over its stored row.
	/// </summary>
	private static async Task<DateOnly?> ResolveActiveCycleDayAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var trackedCycles = context.ChangeTracker.Entries<PolarisCycle>().ToList();
		var trackedActive = trackedCycles
			.Where(entry => entry.State != EntityState.Deleted && entry.Entity.StartTime != null && entry.Entity.EndTime == null)
			.Select(entry => entry.Entity)
			.FirstOrDefault();
		if (trackedActive is not null)
		{
			return TimeframeCandidateService.CycleDay(trackedActive);
		}

		var trackedIds = trackedCycles.Select(entry => entry.Entity.Id).ToList();
		var storedActive = await context.PolarisCycles
			.AsNoTracking()
			.Where(cycle => cycle.StartTime != null && cycle.EndTime == null && !trackedIds.Contains(cycle.Id))
			.OrderByDescending(cycle => cycle.Id)
			.FirstOrDefaultAsync(cancellationToken);
		return storedActive is null ? null : TimeframeCandidateService.CycleDay(storedActive);
	}

	private static bool OrbitCursorNeedsReset(EntityEntry entry, string? orbit)
	{
		return entry.State switch
		{
			// A new orbit-bearing schedule establishes its cursor; a new orbit-less one has nothing to seek.
			EntityState.Added => !string.IsNullOrWhiteSpace(orbit),
			// An orbit edit (set, changed, or cleared) re-anchors the cursor.
			EntityState.Modified => entry.Property(nameof(Fate.Orbit)).IsModified,
			_ => false,
		};
	}

	/// <summary>
	/// Deep-interception enforcement (Strategy 1 / PEP101): a reference to an occurrence must harden it. Every dependency
	/// added in this unit of work, whatever its pathway, has each of its eventive endpoints hardened into the same save, so
	/// the judge and the reconciler resolve a real row rather than reading an absent projection as unsatisfied.
	/// </summary>
	private async Task HardenReferencedOccurrencesAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var addedDependencies = context.ChangeTracker.Entries<Dependency>()
			.Where(entry => entry.State == EntityState.Added)
			.Select(entry => entry.Entity)
			.ToList();
		if (addedDependencies.Count == 0)
		{
			return;
		}

		var hardeningService = serviceProvider.GetRequiredService<OccurrenceHardeningService>();
		foreach (var dependency in addedDependencies)
		{
			await hardeningService.EnsureReferencedEventiveAsync(dependency.Source, cancellationToken);
			await hardeningService.EnsureReferencedEventiveAsync(dependency.Target, cancellationToken);
		}
	}

	/// <summary>
	/// Dependencies are the slave of the entities they join (PEP102 patch 9): in the same unit of work that deletes a
	/// directive, an objective, a fate or a checkpoint, every stored edge touching it — as an endpoint, or as the owner of an
	/// occurrence endpoint — goes with it, so no edge is left naming an id that no longer exists. Riding the save covers every
	/// pathway: the API deletes, a sprint's milestone going with its sprint, and the markdown watcher's delete-from-database
	/// action.
	/// </summary>
	private async Task ReleaseDeletedEndpointDependenciesAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var deletedIds = context.ChangeTracker.Entries()
			.Where(entry => entry.State == EntityState.Deleted && entry.Entity is StellarDirective or Objective or Fate or Checkpoint)
			.Select(entry => ((IPuckNamedEntity)entry.Entity).Id)
			.ToList();
		if (deletedIds.Count == 0)
		{
			return;
		}

		var edges = await context.Dependencies
			.Where(dependency => deletedIds.Contains(dependency.SourceId) || deletedIds.Contains(dependency.TargetId))
			.ToListAsync(cancellationToken);
		if (edges.Count == 0)
		{
			return;
		}

		var auditLog = serviceProvider.GetRequiredService<VaultAuditLogService>();
		foreach (var edge in edges)
		{
			context.Dependencies.Remove(edge);
			auditLog.Stage(
				"state",
				"dependency.release",
				subjectType: nameof(Dependency),
				subjectId: edge.Id.ToString(CultureInfo.InvariantCulture),
				details: new { reason = "endpoint-deleted", source = edge.Source, target = edge.Target, edge.Trigger, edge.Constraint });
		}
	}

	/// <summary>
	/// Purges the stored dependencies the structure now forbids (PEP102 patch 9). A directive's finish depends
	/// intrinsically on the finish of each of its direct children, so moving a directive or an objective under a new
	/// parent — or an end leaving or entering the deferred state, which switches its intrinsic edges on and off — can close
	/// a loop the starts and finishes cannot order. Dependencies are the slave of structure, so the stored edges give way:
	/// of the contradicted ones, those touching the moved subtree are preferred, else any, and the most resolving of them
	/// goes each round (the newest on a tie) until the graph orders cleanly; the ones the intrinsic edges make redundant go
	/// too. Riding the save covers every pathway, the watcher's folder moves included, which can refuse nothing after the
	/// fact.
	/// </summary>
	private async Task PurgeContradictedDependenciesAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var movedDirectiveIds = context.ChangeTracker.Entries<StellarDirective>()
			.Where(entry => StructureChanged(entry, nameof(StellarDirective.ParentDirectiveId), nameof(StellarDirective.Status)))
			.Select(entry => entry.Entity.Id)
			.ToList();
		var movedObjectiveIds = context.ChangeTracker.Entries<Objective>()
			.Where(entry => StructureChanged(entry, nameof(Objective.DirectiveId), nameof(Objective.Status)))
			.Select(entry => entry.Entity.Id)
			.ToList();
		if (movedDirectiveIds.Count == 0 && movedObjectiveIds.Count == 0)
		{
			return;
		}

		var graph = await serviceProvider.GetRequiredService<DependencyGraphService>().LoadGraphAsync(cancellationToken);
		if (graph.Explicit.Count == 0)
		{
			return;
		}

		// The moved subtree: the moved entities and, under a moved directive, everything it carries with it.
		var subtree = new HashSet<string>(movedDirectiveIds.Concat(movedObjectiveIds), StringComparer.Ordinal);
		var directiveParents = await context.Directives
			.OfType<StellarDirective>()
			.AsNoTracking()
			.IgnoreAutoIncludes()
			.Select(directive => new { directive.Id, directive.ParentDirectiveId })
			.ToListAsync(cancellationToken);
		var objectiveParents = await context.Objectives
			.AsNoTracking()
			.IgnoreAutoIncludes()
			.Select(objective => new { objective.Id, objective.DirectiveId })
			.ToListAsync(cancellationToken);
		for (var grew = true; grew;)
		{
			grew = directiveParents.Any(directive => directive.ParentDirectiveId is not null && subtree.Contains(directive.ParentDirectiveId) && subtree.Add(directive.Id));
		}

		subtree.UnionWith(objectiveParents.Where(objective => objective.DirectiveId is not null && subtree.Contains(objective.DirectiveId)).Select(objective => objective.Id));

		var contradicted = DependencyOrdering.PlanPurge(graph.All, candidates =>
		{
			var touching = candidates.Where(candidate => subtree.Contains(candidate.Edge.SourceId) || subtree.Contains(candidate.Edge.TargetId)).ToList();
			return (touching.Count > 0 ? touching : candidates).Select(candidate => candidate.Edge);
		});
		var redundant = DependencyOrdering.Redundant(graph.All).Except(contradicted).ToList();
		if (contradicted.Count == 0 && redundant.Count == 0)
		{
			return;
		}

		var auditLog = serviceProvider.GetRequiredService<VaultAuditLogService>();
		foreach (var (edge, reason) in contradicted.Select(edge => (edge, "contradicted")).Concat(redundant.Select(edge => (edge, "redundant"))))
		{
			context.Dependencies.Remove(edge);
			auditLog.Stage(
				"state",
				"dependency.purge",
				subjectType: nameof(Dependency),
				subjectId: edge.Id.ToString(CultureInfo.InvariantCulture),
				details: new { reason, source = edge.Source, target = edge.Target, edge.Trigger, edge.Constraint, moved = subtree.Where(id => movedDirectiveIds.Contains(id) || movedObjectiveIds.Contains(id)).ToList() });
		}
	}

	/// <summary>
	/// Whether an entry's place in the structure, or the state that switches its intrinsic edges, changed in this
	/// unit of work: it was added under a parent, or its parent or status was modified.
	/// </summary>
	private static bool StructureChanged(EntityEntry entry, string parentProperty, string statusProperty)
	{
		return entry.State switch
		{
			EntityState.Added => entry.Property(parentProperty).CurrentValue is not null,
			EntityState.Modified => entry.Property(parentProperty).IsModified || entry.Property(statusProperty).IsModified,
			_ => false,
		};
	}
}
