using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orbits;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Materialization;
using Pleiades.Puck;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.State;

/// <summary>
/// Applies reusable PLAINTORCH state rules to tracked EF entities before they are persisted.
/// </summary>
/// <remarks>
/// <see cref="OccurrenceHardeningService"/> is resolved lazily from the scope rather than injected: it depends
/// on the <see cref="PlainfraContext"/> whose options wire this processor's interceptor, so a constructor
/// dependency would form a resolution cycle. The scoped context already exists by the time
/// <see cref="ApplyAsync"/> runs, so the lazy resolution binds to the same unit of work.
/// </remarks>
public sealed class PlaintorchStatePolicyProcessor(
	DependencyReconciler dependencyReconciler,
	IServiceProvider serviceProvider)
{
	private const string ObjectiveSettlementDescriptionPrefix = "PLAINTORCH objective settlement";
	private const string AttentiveExecutionDescriptionPrefix = "PLAINTORCH attentive execution";
	private const string DecreeExecutionDescriptionPrefix = "PLAINTORCH decree execution";
	private const string ReflectiveCollectionDescriptionPrefix = "PLAINTORCH reflective collection";

	/// <summary>
	/// The fixed Celestron amount granted when all reflectives of a single Polaris cycle are done (PEP100).
	/// The concrete amount is a placeholder until a proposal pins it down.
	/// </summary>
	public const decimal ReflectiveCollectionReward = 10;

	/// <summary>
	/// Applies all centralized state policies to the supplied context.
	/// </summary>
	public async Task<PlaintorchStatePolicyResult> ApplyAsync(PlainfraContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);

		await ReleaseDeletedIncentiveExecutivesAsync(context, cancellationToken);
		await ClearStaleAvailabilityReferencesAsync(context, cancellationToken);
		await ResetChangedScheduleCursorsAsync(context, cancellationToken);
		await RefreshNextOccurrencesAsync(context, cancellationToken);
		await EnforceOnrushRulesAsync(context, cancellationToken);
		var supersededForecasts = await EnforcePolarisRulesAsync(context, cancellationToken);
		await ApplyObjectiveSettlementRulesAsync(context, cancellationToken);
		await ApplyAttentiveResolutionRulesAsync(context, cancellationToken);
		await ApplyDecreeExecutiveRewardRulesAsync(context, cancellationToken);
		await ApplyReflectiveCollectionRewardRulesAsync(context, cancellationToken);
		await HardenReferencedOccurrencesAsync(context, cancellationToken);
		await dependencyReconciler.ReconcileAsync(context, cancellationToken);

		return supersededForecasts.Count == 0
			? PlaintorchStatePolicyResult.Empty
			: new PlaintorchStatePolicyResult(supersededForecasts);
	}

	/// <summary>
	/// Deleting an incentive keeps the work already recorded against it. In the same unit of work that deletes an
	/// objective or a decree (a fate is never worked), each of its executives in an <em>ended</em> Polaris cycle is kept
	/// with its incentive reference cleared, so the cycle's record of the work — executed, elapsed, allocations — survives
	/// the backlog item; each in the active cycle or a planned/forecast one is removed with it, since that plan can no
	/// longer be worked. Riding the save hook covers every pathway: the API deletes and the markdown watcher's
	/// delete-from-database action, whose delete-blocker check therefore does not count executives
	/// (<see cref="VaultEntityGateway.FindDeleteBlockersAsync"/>).
	/// </summary>
	private static async Task ReleaseDeletedIncentiveExecutivesAsync(PlainfraContext context, CancellationToken cancellationToken)
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
	}

	/// <summary>
	/// Deep-interception availability hygiene (PEP100 patch 2, D9): a directive availability never outlives its
	/// timeframe's role. In the same unit of work that deletes a timeframe, switches one away from
	/// <see cref="TimeframeInclusion.Availability"/>, or deletes a lunar directive (whose timeframes the database cascade
	/// removes out of EF's sight, so their ids are read from the store), every directive pointing at one of those
	/// timeframes is loaded tracked and cleared. Riding the save hook covers any save that deletes a lunar directive or
	/// touches a timeframe, whatever its pathway: the API, the CLI, or the markdown watcher's delete-from-database action.
	/// Note that the watcher does not reach that action for a deleted lunar note today: discovery cannot recover a
	/// Quiet/Freeform note's identity once the file is gone and ignores the removal (an open gap recorded in
	/// <c>core/.DISCUSSION.md</c>); once discovery produces the action, the hook covers it unchanged. The tracked clear is
	/// what the change feed announces; the SetNull foreign key stays only as the database-level backstop.
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
	/// Deep-interception schedule-change rule (Strategy 1): when a fate or decree is created with, or changed
	/// to, a different orbit, its seek cursor is reset to a fresh state anchored today. Riding the save hook
	/// covers every write pathway — API, CLI, scheduler, and the markdown watcher — so editing an orbit in
	/// frontmatter resets the cursor exactly as an API edit does, and the projection recomputes on the new orbit
	/// while already-hardened occurrences persist untouched. Timeframes created with, or changed to, a different
	/// orbit get the same treatment for their orbit state (PEP100 patch 2), except that a recurring timeframe orbit
	/// anchors at the earlier of today and the open Polaris cycle's day, and a timeframe's fixed <c>Z{…}</c> literal is
	/// read on the timeframe calendar (the vault default) rather than as a Gregorian date.
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
			// schedule anchors today so it generates forward with no backfill. The literal is read as Gregorian, as a
			// one-off fate is pinned to that calendar.
			await orbitService.ResetStateAsync(incentive, orbit, ResetEpoch(orbit, today, OrbitDays.Gregorian), cancellationToken);
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
	/// The epoch a reset schedule cursor anchors at: a lone fixed-datetime literal's own date with its year, month and
	/// day read on <paramref name="literalCalendar"/> (Gregorian for a declarative, the vault default calendar for a
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
			.Where(entry => entry.State != EntityState.Deleted && IsActivePolaris(entry.Entity))
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

	/// <summary>
	/// Refreshes the denormalized <see cref="Declarative.NextOccurrence"/> for every declarative touched in this
	/// unit of work (PEP111). Riding the save hook — after the cursor reset above — covers every write pathway, so
	/// creating or editing a schedule (API, CLI, or a frontmatter sync) leaves an accurate cached next occurrence,
	/// and recomputing here restores the database-only field even when a markdown sync would otherwise null it.
	/// </summary>
	private async Task RefreshNextOccurrencesAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var fates = context.ChangeTracker.Entries<Fate>()
			.Where(entry => entry.State is EntityState.Added or EntityState.Modified)
			.Select(entry => entry.Entity)
			.ToList();
		var decrees = context.ChangeTracker.Entries<Decree>()
			.Where(entry => entry.State is EntityState.Added or EntityState.Modified)
			.Select(entry => entry.Entity)
			.ToList();
		if (fates.Count == 0 && decrees.Count == 0)
		{
			return;
		}

		var orbitService = serviceProvider.GetRequiredService<PlaintorchOrbitService>();
		var now = DateTime.Now;
		foreach (var fate in fates)
		{
			// A paused fate (cancelled) generates nothing, so it has no upcoming occurrence; an active or opted-out
			// fate keeps generating and caches its next.
			fate.NextOccurrence = fate.Status is FateStatus.Active or FateStatus.OptOut && !string.IsNullOrWhiteSpace(fate.Orbit)
				? await orbitService.ComputeNextOccurrenceAsync(fate, now, cancellationToken)
				: null;
		}

		foreach (var decree in decrees)
		{
			decree.NextOccurrence = decree.Status == DecreeStatus.Active && !string.IsNullOrWhiteSpace(decree.Orbit)
				? await orbitService.ComputeNextOccurrenceAsync(decree, now, cancellationToken)
				: null;
		}
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
	/// Deep-interception enforcement (Strategy 1 / PEP101): a reference to an occurrence must harden it. Every
	/// dependency added in this unit of work — from any write pathway (API, CLI, scheduler, markdown watcher) —
	/// has each of its eventive endpoints hardened into the same save, so the reconciler resolves a real row
	/// rather than reading an absent projection as unsatisfied.
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

	private static async Task EnforceOnrushRulesAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var candidateEntries = context.ChangeTracker.Entries<OnrushSprint>()
			.Where(entry => entry.State is EntityState.Added or EntityState.Modified)
			.ToList();

		var planningEntries = candidateEntries
			.Where(entry => string.Equals(entry.Entity.Id, OnrushSprint.PlanningPlaceholderId, StringComparison.OrdinalIgnoreCase))
			.ToList();

		if (planningEntries.Count > 1)
		{
			throw new InvalidOperationException("Only one planning onrush sprint may exist at a time.");
		}

		if (planningEntries.Count == 1)
		{
			var planningExists = await context.OnrushSprints
				.AsNoTracking()
				.AnyAsync(sprint => sprint.Id == OnrushSprint.PlanningPlaceholderId, cancellationToken);

			var planningEntry = planningEntries[0];
			if (planningExists && planningEntry.State == EntityState.Added)
			{
				throw new InvalidOperationException("A planning onrush sprint already exists.");
			}
		}

		var activeCandidates = candidateEntries
			.Where(entry => IsActiveOnrush(entry.Entity))
			.ToList();

		if (activeCandidates.Count > 1)
		{
			throw new InvalidOperationException("Only one onrush sprint may be active at a time.");
		}

		if (activeCandidates.Count == 0)
		{
			return;
		}

		var activeCandidate = activeCandidates[0].Entity;
		var activeExists = await context.OnrushSprints
			.AsNoTracking()
			.AnyAsync(sprint => sprint.Id != OnrushSprint.PlanningPlaceholderId
				&& sprint.Id != activeCandidate.Id
				&& sprint.StartDate != null
				&& sprint.EndDate == null,
				cancellationToken);

		if (activeExists)
		{
			throw new InvalidOperationException("Another onrush sprint is already active.");
		}
	}

	private static async Task<List<PolarisCycle>> EnforcePolarisRulesAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var candidateEntries = context.ChangeTracker.Entries<PolarisCycle>()
			.Where(entry => entry.State is EntityState.Added or EntityState.Modified)
			.ToList();

		var activeCandidates = candidateEntries
			.Where(entry => IsActivePolaris(entry.Entity))
			.ToList();

		if (activeCandidates.Count > 1)
		{
			throw new InvalidOperationException("Only one Polaris cycle may be active at a time.");
		}

		if (activeCandidates.Count == 0)
		{
			return [];
		}

		var activeCandidate = activeCandidates[0].Entity;
		var activeExists = await context.PolarisCycles
			.AsNoTracking()
			.AnyAsync(cycle => cycle.Id != activeCandidate.Id
				&& cycle.StartTime != null
				&& cycle.EndTime == null,
				cancellationToken);

		if (activeExists)
		{
			throw new InvalidOperationException("Another Polaris cycle is already active.");
		}

		if (!TryParseCycleDate(activeCandidate.Id, out var activeDate))
		{
			return [];
		}

		var supersededForecasts = await context.PolarisCycles
			.Where(cycle => cycle.Id != activeCandidate.Id && cycle.Forecast != null && cycle.StartTime == null)
			.ToListAsync(cancellationToken);

		var removedForecasts = supersededForecasts
			.Where(forecast => TryParseCycleDate(forecast.Id, out var forecastDate) && forecastDate <= activeDate)
			.ToList();

		if (removedForecasts.Count > 0)
		{
			context.PolarisCycles.RemoveRange(removedForecasts);
		}

		return removedForecasts;
	}

	private static async Task ApplyObjectiveSettlementRulesAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var objectiveEntries = context.ChangeTracker.Entries<Objective>()
			.Where(entry => entry.State is EntityState.Added or EntityState.Modified)
			.ToList();

		if (objectiveEntries.Count == 0)
		{
			return;
		}

		var activeOnrushId = await ResolveActiveOnrushIdAsync(context, cancellationToken);

		foreach (var entry in objectiveEntries)
		{
			var current = entry.Entity;
			var previousStatus = entry.State == EntityState.Added
				? ObjectiveStatus.Standby
				: entry.OriginalValues.GetValue<ObjectiveStatus>(nameof(Objective.Status));

			var wasTerminal = IsSettlementStatus(previousStatus);
			var isTerminal = IsSettlementStatus(current.Status);

			if (wasTerminal == isTerminal)
			{
				continue;
			}

			var existingTransactions = await context.CelestronLedger
				.Where(item => item.SourcePuck == current.Id
					&& item.Description != null
					&& item.Description.StartsWith(ObjectiveSettlementDescriptionPrefix))
				.ToListAsync(cancellationToken);

			if (isTerminal)
			{
				if (existingTransactions.Count > 0)
				{
					continue;
				}

				var isInActiveOnrush = !string.IsNullOrWhiteSpace(activeOnrushId)
					&& string.Equals(activeOnrushId, current.OnrushSprintId, StringComparison.OrdinalIgnoreCase);
				var multiplier = isInActiveOnrush ? 2 : 1;
				context.CelestronLedger.Add(new CelestronTransaction
				{
					Amount = current.CelestronValue * multiplier,
					SourcePuck = current.Id,
					Description = $"{ObjectiveSettlementDescriptionPrefix} ({current.Status})",
				});
				continue;
			}

			if (existingTransactions.Count > 0)
			{
				context.CelestronLedger.RemoveRange(existingTransactions);
			}
		}
	}

	/// <summary>
	/// Records completion time and grants the decree-predefined Celestron reward for each attentive execution
	/// (PEP100). Completion time and reward are recorded when an attentive transitions to Done; both are
	/// cleared or revoked when it leaves Done. Rewards are keyed by occurrence identity (its RECURRENCE-ID, plus
	/// cycle when bound) so repeated occurrences of the same decree reward independently — and, unlike the row id,
	/// that key is already available when an occurrence hardens and resolves in a single save.
	/// </summary>
	private static async Task ApplyAttentiveResolutionRulesAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		// Added covers the Strategy 1 case where interacting with a projected occurrence hardens it and applies the
		// resolution in one save; Modified covers editing an already-hardened row.
		var attentiveEntries = context.ChangeTracker.Entries<Attentive>()
			.Where(entry => entry.State is EntityState.Added or EntityState.Modified)
			.ToList();

		if (attentiveEntries.Count == 0)
		{
			return;
		}

		foreach (var entry in attentiveEntries)
		{
			var current = entry.Entity;
			// A single-save harden-and-resolve enters as Added already carrying its resolution; its prior state is
			// the projected Pending. An existing row edits in as Modified.
			var previousResolution = entry.State == EntityState.Added
				? AttentiveResolution.Pending
				: entry.OriginalValues.GetValue<AttentiveResolution>(nameof(Attentive.Resolution));
			var wasDone = previousResolution == AttentiveResolution.Done;
			var isDone = current.Resolution == AttentiveResolution.Done;

			if (!wasDone && isDone)
			{
				// Stamp the completion time only when the transition did not carry one. A Modified Pending→Done
				// arrives with ResolvedOn cleared, so this assigns now; a directly-inserted or seeded Done row that
				// already knows when it resolved keeps its own timestamp.
				current.ResolvedOn ??= DateTimeOffset.UtcNow;
			}
			else if (!isDone)
			{
				current.ResolvedOn = null;
			}

			if (wasDone == isDone)
			{
				continue;
			}

			var executionDescription = $"{AttentiveExecutionDescriptionPrefix} ({DescribeOccurrence(current)})";
			var existingTransactions = await context.CelestronLedger
				.Where(item => item.SourcePuck == current.DecreeId && item.Description == executionDescription)
				.ToListAsync(cancellationToken);

			if (isDone)
			{
				if (existingTransactions.Count > 0)
				{
					continue;
				}

				var reward = await context.Decrees
					.AsNoTracking()
					.IgnoreAutoIncludes()
					.Where(item => item.Id == current.DecreeId)
					.Select(item => item.ActiveCelestron)
					.FirstOrDefaultAsync(cancellationToken);

				if (reward == 0)
				{
					continue;
				}

				context.CelestronLedger.Add(new CelestronTransaction
				{
					Amount = reward,
					SourcePuck = current.DecreeId,
					Description = executionDescription,
				});
				continue;
			}

			if (existingTransactions.Count > 0)
			{
				context.CelestronLedger.RemoveRange(existingTransactions);
			}
		}
	}

	/// <summary>
	/// Grants a decree-backed executive's Celestron reward on execution (PEP111): executing a decree inside a
	/// Polaris cycle grants the decree's predefined <see cref="Decree.ActiveCelestron"/>, and un-executing it
	/// revokes it — the successor to the Polaris-bound attentive's reward. Objective-backed executives are exempt;
	/// their objective settles its own Celestron on completion. Keyed by decree + cycle (the executive's identity),
	/// so the ledger entry is stable and idempotent across re-saves.
	/// </summary>
	private static async Task ApplyDecreeExecutiveRewardRulesAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var executiveEntries = context.ChangeTracker.Entries<Executive>()
			.Where(entry => entry.State is EntityState.Added or EntityState.Modified)
			.ToList();
		if (executiveEntries.Count == 0)
		{
			return;
		}

		foreach (var entry in executiveEntries)
		{
			var current = entry.Entity;
			if (string.IsNullOrWhiteSpace(current.IncentiveId))
			{
				continue;
			}

			var wasExecuted = entry.State != EntityState.Added
				&& entry.OriginalValues.GetValue<bool>(nameof(Executive.Executed));
			if (wasExecuted == current.Executed)
			{
				continue;
			}

			// Only decree-backed executives carry a per-execution reward; the query returns nothing for an
			// objective-backed one (its incentive id is not a decree), so it is naturally exempt.
			var reward = await context.Decrees
				.AsNoTracking()
				.IgnoreAutoIncludes()
				.Where(item => item.Id == current.IncentiveId)
				.Select(item => (int?)item.ActiveCelestron)
				.FirstOrDefaultAsync(cancellationToken);
			if (reward is not { } amount || amount == 0)
			{
				continue;
			}

			var description = $"{DecreeExecutionDescriptionPrefix} (cycle {current.PolarisCycleId})";
			var existingTransactions = await context.CelestronLedger
				.Where(item => item.SourcePuck == current.IncentiveId && item.Description == description)
				.ToListAsync(cancellationToken);

			if (current.Executed)
			{
				if (existingTransactions.Count == 0)
				{
					context.CelestronLedger.Add(new CelestronTransaction
					{
						Amount = amount,
						SourcePuck = current.IncentiveId,
						Description = description,
					});
				}
			}
			else if (existingTransactions.Count > 0)
			{
				context.CelestronLedger.RemoveRange(existingTransactions);
			}
		}
	}

	/// <summary>
	/// Applies the reflective collection reward (PEP100): reflectives ignore decree rewards; instead, once
	/// all reflectives of a single Polaris cycle are done, the whole collection grants a fixed Celestron
	/// amount. Un-executing a reflective revokes the cycle's collection reward.
	/// </summary>
	private static async Task ApplyReflectiveCollectionRewardRulesAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var affectedCycleIds = context.ChangeTracker.Entries<Reflective>()
			.Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
			.Select(entry => entry.Entity.PolarisCycleId)
			.Where(cycleId => !string.IsNullOrWhiteSpace(cycleId))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		foreach (var cycleId in affectedCycleIds)
		{
			var storedStates = await context.Set<Reflective>()
				.AsNoTracking()
				.Where(item => item.PolarisCycleId == cycleId)
				.Select(item => new { item.Id, item.Executed })
				.ToListAsync(cancellationToken);

			// Overlay tracked changes on top of the stored snapshot so the decision reflects this save.
			var effective = storedStates.ToDictionary(item => item.Id, item => item.Executed);
			foreach (var entry in context.ChangeTracker.Entries<Reflective>()
				.Where(entry => string.Equals(entry.Entity.PolarisCycleId, cycleId, StringComparison.OrdinalIgnoreCase)))
			{
				if (entry.State == EntityState.Deleted)
				{
					effective.Remove(entry.Entity.Id);
				}
				else
				{
					effective[entry.Entity.Id] = entry.Entity.Executed;
				}
			}

			var allDone = effective.Count > 0 && effective.Values.All(executed => executed);
			var collectionDescription = $"{ReflectiveCollectionDescriptionPrefix} (cycle {cycleId})";
			var existingTransactions = await context.CelestronLedger
				.Where(item => item.SourcePuck == cycleId && item.Description == collectionDescription)
				.ToListAsync(cancellationToken);

			if (allDone)
			{
				if (existingTransactions.Count == 0)
				{
					context.CelestronLedger.Add(new CelestronTransaction
					{
						Amount = ReflectiveCollectionReward,
						SourcePuck = cycleId,
						Description = collectionDescription,
					});
				}

				continue;
			}

			if (existingTransactions.Count > 0)
			{
				context.CelestronLedger.RemoveRange(existingTransactions);
			}
		}
	}

	/// <summary>
	/// Describes an attentive occurrence by its RECURRENCE-ID for a stable, row-id-free reward ledger key — the
	/// occurrence's identity survives a reschedule and is known before the row is saved.
	/// </summary>
	private static string DescribeOccurrence(Attentive attentive)
	{
		// The key format predates the single RECURRENCE-ID and is persisted in the ledger, so it is kept: the date
		// alone for a midnight (all-day) slot, the date and minute otherwise.
		var slot = attentive.RecurrenceId.TimeOfDay == TimeSpan.Zero
			? attentive.RecurrenceId.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
			: attentive.RecurrenceId.ToString("yyyy-MM-dd'T'HH\\:mm", CultureInfo.InvariantCulture);
		return $"attentive {slot}";
	}

	private static async Task<string?> ResolveActiveOnrushIdAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var trackedActive = context.ChangeTracker.Entries<OnrushSprint>()
			.Where(entry => entry.State != EntityState.Deleted && IsActiveOnrush(entry.Entity))
			.Select(entry => entry.Entity.Id)
			.FirstOrDefault();

		if (!string.IsNullOrWhiteSpace(trackedActive))
		{
			return trackedActive;
		}

		return await context.OnrushSprints
			.AsNoTracking()
			.Where(sprint => sprint.Id != OnrushSprint.PlanningPlaceholderId && sprint.StartDate != null && sprint.EndDate == null)
			.OrderByDescending(sprint => sprint.StartDate)
			.Select(sprint => sprint.Id)
			.FirstOrDefaultAsync(cancellationToken);
	}

	private static bool IsActiveOnrush(OnrushSprint sprint)
	{
		return sprint.Id != OnrushSprint.PlanningPlaceholderId && sprint.StartDate != null && sprint.EndDate == null;
	}

	private static bool IsActivePolaris(PolarisCycle cycle)
	{
		return cycle.StartTime != null && cycle.EndTime == null;
	}

	private static bool IsSettlementStatus(ObjectiveStatus status)
	{
		return status is ObjectiveStatus.Done or ObjectiveStatus.Archived or ObjectiveStatus.Failed;
	}

	private static bool TryParseCycleDate(string cycleId, out DateOnly date)
	{
		// A Polaris cycle id is a Pleiadean date stamp ({D:p}); parsing it as Gregorian yyyyMMdd always failed, silently
		// disabling forecast supersession (an activated cycle is meant to drop stale forecasts on or before its date).
		try
		{
			date = PuckDateStampCodec.Parse(cycleId, PuckDateStampKind.Pleiadean);
			return true;
		}
		catch (Exception exception) when (exception is FormatException or ArgumentException or OverflowException)
		{
			date = default;
			return false;
		}
	}
}