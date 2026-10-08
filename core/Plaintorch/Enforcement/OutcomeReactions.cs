using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Preferences;
using Pleiades.Puck;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Enforcement;

/// <summary>
/// The engine's third phase (PEP103): what the final state of a save implies, once it is judged and any compensations are
/// made, so a compensation is settled, rewarded and reconciled like a requested change.
/// </summary>
/// <remarks>
/// In order:
/// <list type="number">
/// <item><description>Every fate or decree in the save gets its cached next occurrence recomputed (SH-07).</description></item>
/// <item><description>An active Polaris cycle supersedes the unstarted forecasts on or before its day (SH-11).</description></item>
/// <item><description>Objectives settle their Celestron value on entering Done, Archived or Failed, and leaving revokes it
/// (SH-12).</description></item>
/// <item><description>Attentives carry their resolution time and grant their decree's reward while Done (SH-13, SH-14).</description></item>
/// <item><description>Executing a decree-backed executive or reflective grants the decree's reward (SH-15, SH-29), and a
/// cycle whose reflectives are all executed grants the collection reward (SH-16).</description></item>
/// <item><description>Every stored dependency's satisfaction and every checkpoint's unlock are recomputed (SH-21, SH-22).</description></item>
/// </list>
/// Context-bound services are resolved from the scope when needed, for the reason <see cref="StateEngine"/> gives.
/// </remarks>
public sealed class OutcomeReactions(
	DependencyReconciler dependencyReconciler,
	IOptionsSnapshot<ReflectionPreferences> reflectionPreferences,
	IServiceProvider serviceProvider)
{
	private const string ObjectiveSettlementDescriptionPrefix = "PLAINTORCH objective settlement";
	private const string AttentiveExecutionDescriptionPrefix = "PLAINTORCH attentive execution";
	private const string DecreeExecutionDescriptionPrefix = "PLAINTORCH decree execution";
	private const string ReflectiveExecutionDescriptionPrefix = "PLAINTORCH reflective execution";
	private const string ReflectiveCollectionDescriptionPrefix = "PLAINTORCH reflective collection";

	/// <summary>
	/// Applies every outcome reaction to the save.
	/// </summary>
	public async Task ApplyAsync(StatePass pass, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(pass);
		var context = pass.Context;
		await RefreshNextOccurrencesAsync(context, cancellationToken);
		pass.Supersede(await SupersedeForecastsAsync(context, cancellationToken));
		await ApplyObjectiveSettlementRulesAsync(context, cancellationToken);
		await ApplyAttentiveResolutionRulesAsync(context, cancellationToken);
		await ApplyDecreeExecutiveRewardRulesAsync(context, cancellationToken);
		await ApplyReflectiveDecreeRewardRulesAsync(context, cancellationToken);
		await ApplyReflectiveCollectionRewardRulesAsync(context, reflectionPreferences.Value.DailyReward, cancellationToken);
		await dependencyReconciler.ReconcileAsync(context, cancellationToken);
	}

	/// <summary>
	/// While a cycle is active, every unstarted forecast dated on or before its day is deleted with its executives and
	/// reflectives (SH-11); the notes go once the save commits. That only one cycle is active is the judge's rule
	/// (<see cref="StateJudge.SingleActivePolarisRule"/>), so a save that reaches this has at most one.
	/// </summary>
	private static async Task<List<PolarisCycle>> SupersedeForecastsAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var activeCandidate = context.ChangeTracker.Entries<PolarisCycle>()
			.Where(entry => entry.State is EntityState.Added or EntityState.Modified && IsActivePolaris(entry.Entity))
			.Select(entry => entry.Entity)
			.FirstOrDefault();
		if (activeCandidate is null || !TryParseCycleDate(activeCandidate.Id, out var activeDate))
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

	/// <summary>
	/// Refreshes the denormalized <see cref="Declarative.NextOccurrence"/> for every declarative touched in this
	/// unit of work (PEP111). Riding the save — after the structural phase reset or sought the cursors — covers every write pathway, so
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
		var now = DateTimeOffset.Now;
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

	/// <summary>
	/// Settles an objective (PEP100): entering Done, Archived or Failed earns its Celestron value once, doubled while it
	/// belongs to the active onrush sprint, and leaving those statuses revokes it.
	/// </summary>
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
	/// Grants a decree-backed reflective's per-run Celestron reward (PEP104): executing a reflective generated from a
	/// decree grants that decree's <see cref="Decree.ActiveCelestron"/>, on top of the cycle's collection reward, and
	/// un-executing it revokes it. A reflective without a decree grants nothing here. Keyed by decree + cycle, since a
	/// decree yields at most one reflective per cycle, so the ledger entry is stable and idempotent across re-saves.
	/// </summary>
	private static async Task ApplyReflectiveDecreeRewardRulesAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var reflectiveEntries = context.ChangeTracker.Entries<Reflective>()
			.Where(entry => entry.State is EntityState.Added or EntityState.Modified)
			.ToList();
		foreach (var entry in reflectiveEntries)
		{
			var current = entry.Entity;
			if (string.IsNullOrWhiteSpace(current.DecreeId))
			{
				continue;
			}

			var wasExecuted = entry.State != EntityState.Added
				&& entry.OriginalValues.GetValue<bool>(nameof(Reflective.Executed));
			if (wasExecuted == current.Executed)
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

			var description = $"{ReflectiveExecutionDescriptionPrefix} (cycle {current.PolarisCycleId})";
			var existingTransactions = await context.CelestronLedger
				.Where(item => item.SourcePuck == current.DecreeId && item.Description == description)
				.ToListAsync(cancellationToken);

			if (current.Executed)
			{
				if (existingTransactions.Count == 0)
				{
					context.CelestronLedger.Add(new CelestronTransaction
					{
						Amount = reward,
						SourcePuck = current.DecreeId,
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
	/// Applies the reflective collection reward (PEP100, PEP104): once all reflectives of a single Polaris cycle are
	/// done, the whole collection grants <paramref name="dailyReward"/> Celestron
	/// (<see cref="ReflectionPreferences.DailyReward"/>), once. Un-executing a reflective revokes the cycle's collection
	/// reward. A reward of 0 or less grants nothing.
	/// </summary>
	private static async Task ApplyReflectiveCollectionRewardRulesAsync(PlainfraContext context, int dailyReward, CancellationToken cancellationToken)
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
				if (existingTransactions.Count == 0 && dailyReward > 0)
				{
					context.CelestronLedger.Add(new CelestronTransaction
					{
						Amount = dailyReward,
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
		// An attentive is always schedule-born, so its RECURRENCE-ID is present (the column is required).
		var recurrenceId = attentive.RecurrenceId!.Value;
		var slot = recurrenceId.TimeOfDay == TimeSpan.Zero
			? recurrenceId.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
			: recurrenceId.ToString("yyyy-MM-dd'T'HH\\:mm", CultureInfo.InvariantCulture);
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
