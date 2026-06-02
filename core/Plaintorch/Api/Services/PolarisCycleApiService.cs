using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Plaintorch.State;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Api.Services;

/// <summary>
/// Implements the Polaris cycle-facing PLAINTORCH application API.
/// </summary>
public sealed class PolarisCycleApiService(
	PlainfraContext context,
	PuckCreationService puckCreationService,
	PolarisCycleLifecycle lifecycle,
	PlaintorchStateService stateService,
	PlaintorchMarkdownStorageService markdownFileService,
	VaultAuditLogService auditLogService) : IPolarisCycleApi
{
	/// <inheritdoc />
	public async Task<PolarisCycle> PlanAsync(DateOnly forecastReference, int daysAhead, string? body = null, CancellationToken cancellationToken = default)
	{
		var targetDate = forecastReference.AddDays(daysAhead);
		var id = puckCreationService.CreateIdFor<PolarisCycle>(systemSegments: [new PuckSegmentInput(Date: targetDate)]);
		var cycle = lifecycle.PlanForecast(forecastReference, daysAhead, id);

		context.PolarisCycles.Add(cycle);
		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SavePolarisCycleAsync(cycle, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "polaris.plan", subject: cycle, cancellationToken: cancellationToken);
		return cycle;
	}

	/// <inheritdoc />
	public async Task<PolarisCycle> BeginAsync(string? polarisCycleId = null, DateTimeOffset? startTime = null, CancellationToken cancellationToken = default)
	{
		var cycle = await ResolveCycleForMutationAsync(polarisCycleId, requireTodayFallback: true, cancellationToken)
			?? throw new InvalidOperationException("No Polaris cycle is available to begin.");

		if (cycle.StartTime is not null && cycle.EndTime is null)
		{
			return cycle;
		}

		var previous = Clone(cycle);
		var started = lifecycle.Start(cycle, startTime ?? DateTimeOffset.UtcNow);
		ApplyCycle(cycle, started);
		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SavePolarisCycleAsync(cycle, previous, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "polaris.begin", subject: cycle, cancellationToken: cancellationToken);
		return cycle;
	}

	/// <inheritdoc />
	public async Task<PolarisCycle> StartNewAsync(DateTimeOffset? startTime = null, string? body = null, CancellationToken cancellationToken = default)
	{
		var resolvedStartTime = startTime ?? DateTimeOffset.UtcNow;
		var targetDate = DateOnly.FromDateTime(resolvedStartTime.LocalDateTime);
		var cycle = new PolarisCycle
		{
			Id = puckCreationService.CreateIdFor<PolarisCycle>(systemSegments: [new PuckSegmentInput(Date: targetDate)]),
			Title = PlaintorchDefaultTitleFactory.CreatePolarisTitle(targetDate),
			StartTime = resolvedStartTime,
		};

		context.PolarisCycles.Add(cycle);
		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SavePolarisCycleAsync(cycle, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "polaris.start-new", subject: cycle, cancellationToken: cancellationToken);
		return cycle;
	}

	/// <inheritdoc />
	public async Task<PolarisCycle> EndAsync(string? polarisCycleId = null, DateTimeOffset? endTime = null, CancellationToken cancellationToken = default)
	{
		var cycle = await ResolveCycleForMutationAsync(polarisCycleId, requireTodayFallback: false, cancellationToken)
			?? throw new InvalidOperationException("No Polaris cycle is available to end.");

		var previous = Clone(cycle);
		var finished = lifecycle.Finish(cycle, endTime ?? DateTimeOffset.UtcNow);
		ApplyCycle(cycle, finished);
		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SavePolarisCycleAsync(cycle, previous, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "polaris.end", subject: cycle, cancellationToken: cancellationToken);
		return cycle;
	}

	/// <inheritdoc />
	public async Task<PolarisCycle?> GetAsync(string? polarisCycleId = null, CancellationToken cancellationToken = default)
	{
		if (!string.IsNullOrWhiteSpace(polarisCycleId))
		{
			return await context.PolarisCycles
				.AsNoTracking()
				.Include(cycle => cycle.Executives)
					.ThenInclude(executive => executive.Objective)
				.FirstOrDefaultAsync(cycle => cycle.Id == polarisCycleId, cancellationToken);
		}

		var activeCycle = await stateService.GetActivePolarisCycleAsync(cancellationToken);
		if (activeCycle is null)
		{
			return null;
		}

		return await context.PolarisCycles
			.AsNoTracking()
			.Include(cycle => cycle.Executives)
				.ThenInclude(executive => executive.Objective)
			.FirstOrDefaultAsync(cycle => cycle.Id == activeCycle.Id, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<PolarisCycle>> ListForecastsAsync(CancellationToken cancellationToken = default)
	{
		return await context.PolarisCycles
			.AsNoTracking()
			.Include(cycle => cycle.Executives)
				.ThenInclude(executive => executive.Objective)
			.Where(cycle => cycle.Forecast != null && cycle.StartTime == null)
			.OrderBy(cycle => cycle.Id)
			.ToListAsync(cancellationToken);
	}

	/// <inheritdoc />
	public async Task<PolarisExecutivePlanResult> PlanExecutiveAsync(PolarisExecutivePlan plan, string? polarisCycleId = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(plan);
		var cycle = await ResolveCycleForMutationAsync(polarisCycleId, requireTodayFallback: false, cancellationToken)
			?? throw new InvalidOperationException("No Polaris cycle is available for executive planning.");

		Objective? objective = null;
		string? executiveTitle;
		switch (plan.Mode)
		{
			case PolarisExecutivePlanningMode.OneShot:
				executiveTitle = plan.ExecutiveTitle ?? plan.Title;
				ArgumentException.ThrowIfNullOrWhiteSpace(executiveTitle);
				break;

			case PolarisExecutivePlanningMode.Standalone:
				ArgumentException.ThrowIfNullOrWhiteSpace(plan.Title);
				objective = await CreateObjectiveAsync(plan.Title!, null, plan.OnrushSprintId, plan.College, plan.CelestronValue, plan.ObjectiveIsEnduring, cancellationToken);
				executiveTitle = plan.ExecutiveTitle ?? objective.Title;
				break;

			case PolarisExecutivePlanningMode.FromDirective:
				ArgumentException.ThrowIfNullOrWhiteSpace(plan.DirectiveId);
				ArgumentException.ThrowIfNullOrWhiteSpace(plan.Title);
				objective = await CreateObjectiveAsync(plan.Title!, plan.DirectiveId, plan.OnrushSprintId, plan.College, plan.CelestronValue, plan.ObjectiveIsEnduring, cancellationToken);
				executiveTitle = plan.ExecutiveTitle ?? objective.Title;
				break;

			case PolarisExecutivePlanningMode.FromObjective:
				ArgumentException.ThrowIfNullOrWhiteSpace(plan.ObjectiveId);
				objective = await context.Objectives.FirstOrDefaultAsync(item => item.Id == plan.ObjectiveId, cancellationToken)
					?? throw new InvalidOperationException($"Objective '{plan.ObjectiveId}' was not found.");
				if (plan.College is not null)
				{
					objective.College = plan.College.Value;
				}

				if (plan.CelestronValue is not null)
				{
					objective.CelestronValue = plan.CelestronValue.Value;
				}

				if (!string.IsNullOrWhiteSpace(plan.OnrushSprintId))
				{
					objective.OnrushSprintId = plan.OnrushSprintId;
				}

				objective.Status = ObjectiveStatus.Polaris;
				executiveTitle = plan.ExecutiveTitle ?? objective.Title;
				break;

			default:
				throw new InvalidOperationException($"Unsupported executive planning mode '{plan.Mode}'.");
		}

		var executive = new Executive
		{
			PolarisCycleId = cycle.Id,
			ObjectiveId = objective?.Id,
			Executed = false,
		};

		context.Add(executive);
		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"polaris.plan-executive",
			subjectType: nameof(Executive),
			subjectId: executive.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
			details: new { cycleId = cycle.Id, objectiveId = objective?.Id, mode = plan.Mode.ToString() },
			cancellationToken: cancellationToken);
		return new PolarisExecutivePlanResult(objective, executive);
	}

	/// <inheritdoc />
	public async Task<Executive> UpdateExecutiveAsync(long executiveId, ExecutiveUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(update);
		var executive = await context.Set<Executive>().FirstOrDefaultAsync(item => item.Id == executiveId, cancellationToken)
			?? throw new InvalidOperationException($"Executive '{executiveId}' was not found.");

		if (update.Executed is not null)
		{
			executive.Executed = update.Executed.Value;
		}

		if (!string.IsNullOrWhiteSpace(update.ObjectiveId))
		{
			executive.ObjectiveId = update.ObjectiveId;
		}

		if (update.ClearObjective)
		{
			executive.ObjectiveId = null;
		}

		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"polaris.update-executive",
			subjectType: nameof(Executive),
			subjectId: executive.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
			details: new { objectiveId = executive.ObjectiveId, executed = executive.Executed },
			cancellationToken: cancellationToken);
		return executive;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Reflective>> DrawReflectivesAsync(ReflectiveDrawRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (request.Count < 1)
		{
			throw new ArgumentOutOfRangeException(nameof(request), "At least one reflective must be requested.");
		}

		var cycle = await ResolveCycleForMutationAsync(request.PolarisCycleId, requireTodayFallback: false, cancellationToken)
			?? throw new InvalidOperationException("No Polaris cycle is available for reflective drawing.");

		var existingCount = await context.Set<Reflective>()
			.CountAsync(item => item.PolarisCycleId == cycle.Id, cancellationToken);

		var created = new List<Reflective>();
		for (var index = 0; index < request.Count; index++)
		{
			var reflective = new Reflective
			{
				Description = $"Reflective prompt {existingCount + index + 1} for {cycle.Title}",
				PolarisCycleId = cycle.Id,
				Executed = false,
			};

			context.Add(reflective);
			created.Add(reflective);
		}

		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"polaris.draw-reflectives",
			subjectType: nameof(PolarisCycle),
			subjectId: cycle.Id,
			subjectTitle: cycle.Title,
			details: new { count = created.Count },
			cancellationToken: cancellationToken);
		return created;
	}

	/// <inheritdoc />
	public async Task<Reflective> UpdateReflectiveAsync(long reflectiveId, ReflectiveUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(update);
		var reflective = await context.Set<Reflective>().FirstOrDefaultAsync(item => item.Id == reflectiveId, cancellationToken)
			?? throw new InvalidOperationException($"Reflective '{reflectiveId}' was not found.");

		if (!string.IsNullOrWhiteSpace(update.Description))
		{
			reflective.Description = update.Description;
		}

		if (update.Executed is not null)
		{
			reflective.Executed = update.Executed.Value;
		}

		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"polaris.update-reflective",
			subjectType: nameof(Reflective),
			subjectId: reflective.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
			subjectTitle: reflective.Description,
			cancellationToken: cancellationToken);
		return reflective;
	}

	private async Task<PolarisCycle?> ResolveCycleForMutationAsync(string? cycleId, bool requireTodayFallback, CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(cycleId))
		{
			return await context.PolarisCycles.FirstOrDefaultAsync(item => item.Id == cycleId, cancellationToken);
		}

		var active = await context.PolarisCycles
			.FirstOrDefaultAsync(item => item.StartTime != null && item.EndTime == null, cancellationToken);
		if (active is not null)
		{
			return active;
		}

		if (!requireTodayFallback)
		{
			return await context.PolarisCycles.FirstOrDefaultAsync(item => item.Id == DateOnly.FromDateTime(DateTime.Today).ToString("yyyyMMdd", CultureInfo.InvariantCulture), cancellationToken);
		}

		return await context.PolarisCycles.FirstOrDefaultAsync(item => item.Id == DateOnly.FromDateTime(DateTime.Today).ToString("yyyyMMdd", CultureInfo.InvariantCulture), cancellationToken);
	}


	private async Task<Objective> CreateObjectiveAsync(string title, string? directiveId, string? onrushSprintId, ObjectiveCollege? college, int? celestronValue, bool isEnduring, CancellationToken cancellationToken)
	{
		var objective = new Objective
		{
			Id = puckCreationService.CreateIdFor<Objective>(),
			Title = title,
			DirectiveId = directiveId,
			OnrushSprintId = onrushSprintId,
			College = college ?? ObjectiveCollege.Unspecified,
			CelestronValue = celestronValue ?? 0,
			IsEnduring = isEnduring,
			Status = ObjectiveStatus.Polaris,
		};

		context.Objectives.Add(objective);
		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SaveObjectiveAsync(objective, cancellationToken: cancellationToken);
		return objective;
	}

	private static PolarisCycle Clone(PolarisCycle cycle)
	{
		return new PolarisCycle
		{
			Id = cycle.Id,
			Title = cycle.Title,
			Forecast = cycle.Forecast is null
				? null
				: new PolarisForecast
				{
					ForecastReference = cycle.Forecast.ForecastReference,
					ForecastTarget = cycle.Forecast.ForecastTarget,
				},
			StartTime = cycle.StartTime,
			EndTime = cycle.EndTime,
		};
	}

	private static void ApplyCycle(PolarisCycle target, PolarisCycle source)
	{
		target.Title = source.Title;
		target.Forecast = source.Forecast;
		target.StartTime = source.StartTime;
		target.EndTime = source.EndTime;
	}
}