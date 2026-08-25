using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Pleiades.Orchestration;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.State;

/// <summary>
/// Applies reusable PLAINTORCH state rules to tracked EF entities before they are persisted.
/// </summary>
public sealed class PlaintorchStatePolicyProcessor(DependencyReconciler dependencyReconciler)
{
	private const string ObjectiveSettlementDescriptionPrefix = "PLAINTORCH objective settlement";
	private const string AttentiveExecutionDescriptionPrefix = "PLAINTORCH attentive execution";
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

		await EnforceOnrushRulesAsync(context, cancellationToken);
		var supersededForecasts = await EnforcePolarisRulesAsync(context, cancellationToken);
		await ApplyObjectiveSettlementRulesAsync(context, cancellationToken);
		await ApplyAttentiveResolutionRulesAsync(context, cancellationToken);
		await ApplyReflectiveCollectionRewardRulesAsync(context, cancellationToken);
		await dependencyReconciler.ReconcileAsync(context, cancellationToken);

		return supersededForecasts.Count == 0
			? PlaintorchStatePolicyResult.Empty
			: new PlaintorchStatePolicyResult(supersededForecasts);
	}

	private static async Task EnforceOnrushRulesAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var candidateEntries = context.ChangeTracker.Entries<OnrushSprint>()
			.Where(entry => entry.State is EntityState.Added or EntityState.Modified)
			.ToList();

		var planningEntries = candidateEntries
			.Where(entry => string.Equals(entry.Entity.Id, "0", StringComparison.OrdinalIgnoreCase))
			.ToList();

		if (planningEntries.Count > 1)
		{
			throw new InvalidOperationException("Only one planning onrush sprint may exist at a time.");
		}

		if (planningEntries.Count == 1)
		{
			var planningExists = await context.OnrushSprints
				.AsNoTracking()
				.AnyAsync(sprint => sprint.Id == "0", cancellationToken);

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
			.AnyAsync(sprint => sprint.Id != "0"
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
	/// cleared or revoked when it leaves Done. Rewards are keyed per attentive instance so repeated occurrences
	/// of the same decree reward independently.
	/// </summary>
	private static async Task ApplyAttentiveResolutionRulesAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var attentiveEntries = context.ChangeTracker.Entries<Attentive>()
			.Where(entry => entry.State == EntityState.Modified)
			.ToList();

		if (attentiveEntries.Count == 0)
		{
			return;
		}

		foreach (var entry in attentiveEntries)
		{
			var current = entry.Entity;
			var previousResolution = entry.OriginalValues.GetValue<AttentiveResolution>(nameof(Attentive.Resolution));
			var wasDone = previousResolution == AttentiveResolution.Done;
			var isDone = current.Resolution == AttentiveResolution.Done;

			if (!wasDone && isDone)
			{
				current.ResolvedOn = DateTimeOffset.UtcNow;
			}
			else if (!isDone)
			{
				current.ResolvedOn = null;
			}

			if (wasDone == isDone)
			{
				continue;
			}

			var executionDescription = $"{AttentiveExecutionDescriptionPrefix} (attentive {current.Id.ToString(CultureInfo.InvariantCulture)})";
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
			.Where(sprint => sprint.Id != "0" && sprint.StartDate != null && sprint.EndDate == null)
			.OrderByDescending(sprint => sprint.StartDate)
			.Select(sprint => sprint.Id)
			.FirstOrDefaultAsync(cancellationToken);
	}

	private static bool IsActiveOnrush(OnrushSprint sprint)
	{
		return sprint.Id != "0" && sprint.StartDate != null && sprint.EndDate == null;
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
		return DateOnly.TryParseExact(cycleId, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
	}
}