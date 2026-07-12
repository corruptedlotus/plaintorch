using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Pleiades.Orchestration;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.State;

/// <summary>
/// Applies reusable PLAINTORCH state rules to tracked EF entities before they are persisted.
/// </summary>
public sealed class PlaintorchStatePolicyProcessor
{
	private const string ObjectiveSettlementDescriptionPrefix = "PLAINTORCH objective settlement";

	/// <summary>
	/// Applies all centralized state policies to the supplied context.
	/// </summary>
	public async Task<PlaintorchStatePolicyResult> ApplyAsync(PlainfraContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);

		await EnforceOnrushRulesAsync(context, cancellationToken);
		var supersededForecasts = await EnforcePolarisRulesAsync(context, cancellationToken);
		await ApplyObjectiveSettlementRulesAsync(context, cancellationToken);

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

				var multiplier = string.Equals(activeOnrushId, current.OnrushSprintId, StringComparison.OrdinalIgnoreCase) ? 2 : 1;
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