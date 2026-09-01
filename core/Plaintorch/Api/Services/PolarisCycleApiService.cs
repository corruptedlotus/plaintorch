using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Plaintorch.Materialization;
using Pleiades.Plaintorch.State;
using Pleiades.Vault.Database;
using Pleiades.Vault.Media;

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
	ProximityMaterializationService materializationService,
	TimeframeAffinityResolver affinityResolver,
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
		await materializationService.MaterializeForCycleAsync(cycle, cancellationToken);
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
		await materializationService.MaterializeForCycleAsync(cycle, cancellationToken);
		await markdownFileService.SavePolarisCycleAsync(cycle, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "polaris.start-new", subject: cycle, cancellationToken: cancellationToken);
		return cycle;
	}

	/// <inheritdoc />
	public async Task<PolarisCycle> UpdateAsync(string polarisCycleId, PolarisCycleUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(polarisCycleId);
		ArgumentNullException.ThrowIfNull(update);

		var cycle = await context.PolarisCycles.FirstOrDefaultAsync(item => item.Id == polarisCycleId, cancellationToken)
			?? throw new InvalidOperationException($"Polaris cycle '{polarisCycleId}' was not found.");
		var previous = Clone(cycle);

		if (!string.IsNullOrWhiteSpace(update.Title))
		{
			cycle.Title = update.Title;
		}

		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SavePolarisCycleAsync(cycle, previous, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "polaris.update", subject: cycle, cancellationToken: cancellationToken);
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
		string? targetId = polarisCycleId;
		if (string.IsNullOrWhiteSpace(targetId))
		{
			var activeCycle = await stateService.GetActivePolarisCycleAsync(cancellationToken);
			if (activeCycle is null)
			{
				return null;
			}

			targetId = activeCycle.Id;
		}

		var cycle = await context.PolarisCycles
			.AsNoTracking()
			.Include(item => item.Executives)
				.ThenInclude(executive => executive.Objective)
			.Include(item => item.Executives)
				.ThenInclude(executive => executive.AffinityTimeframe)
			.Include(item => item.Reflectives)
				.ThenInclude(reflective => reflective.Decree)
			.Include(item => item.Attentives)
				.ThenInclude(attentive => attentive.Decree)
			.FirstOrDefaultAsync(item => item.Id == targetId, cancellationToken);

		return cycle;
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
			Estimation = plan.Estimation,
			Minimum = plan.Minimum,
			Maximum = plan.Maximum,
			// Auto-inclusion: an executive built from an objective inherits its affinity from the objective's
			// college (PEP100 patch). One-shot executives carry no objective and so no auto-affinity.
			AffinityTimeframeId = objective is null
				? null
				: await affinityResolver.ResolveForCollegeAsync(objective.College, cancellationToken),
		};
		executive.NormalizeTimeAllocations();

		context.Add(executive);
		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"polaris.plan-executive",
			subjectType: nameof(Executive),
			subjectId: executive.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
			details: new { cycleId = cycle.Id, objectiveId = objective?.Id, mode = plan.Mode.ToString(), estimation = executive.Estimation, minimum = executive.Minimum, maximum = executive.Maximum },
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

		// The executive's objective can be reassigned but never cleared — an executive without an objective has
		// nothing to work at.
		if (!string.IsNullOrWhiteSpace(update.ObjectiveId))
		{
			executive.ObjectiveId = update.ObjectiveId;
		}

		// A set allocation applies its value — including null, which clears it; an unset one is left unchanged.
		if (update.Estimation.IsSet)
		{
			executive.Estimation = update.Estimation.Value;
		}

		if (update.Minimum.IsSet)
		{
			executive.Minimum = update.Minimum.Value;
		}

		if (update.Maximum.IsSet)
		{
			executive.Maximum = update.Maximum.Value;
		}

		if (update.Elapsed is not null)
		{
			executive.Elapsed = update.Elapsed.Value;
		}

		if (update.AffinityTimeframeId.IsSet)
		{
			if (update.AffinityTimeframeId.Value is long timeframeId)
			{
				var timeframeExists = await context.Timeframes.AnyAsync(item => item.Id == timeframeId, cancellationToken);
				if (!timeframeExists)
				{
					throw new InvalidOperationException($"Timeframe '{timeframeId}' was not found.");
				}

				executive.AffinityTimeframeId = timeframeId;
			}
			else
			{
				executive.AffinityTimeframeId = null;
			}
		}

		executive.NormalizeTimeAllocations();

		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"polaris.update-executive",
			subjectType: nameof(Executive),
			subjectId: executive.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
			details: new { objectiveId = executive.ObjectiveId, executed = executive.Executed, estimation = executive.Estimation, minimum = executive.Minimum, maximum = executive.Maximum, elapsed = executive.Elapsed },
			cancellationToken: cancellationToken);
		return executive;
	}

	/// <inheritdoc />
	public async Task<PolarisCycleInclusions> GetInclusionsAsync(string? polarisCycleId = null, CancellationToken cancellationToken = default)
	{
		var cycle = await ResolveCycleForMutationAsync(polarisCycleId, requireTodayFallback: false, cancellationToken)
			?? throw new InvalidOperationException("No Polaris cycle is available for inclusion listing.");

		if (cycle.StartTime is null)
		{
			return new PolarisCycleInclusions([], []);
		}

		var (windowStart, windowEnd) = InclusionWindow.Resolve(cycle);
		var candidateDates = InclusionWindow.EnumerateDates(windowStart, windowEnd);

		var eventives = await context.Eventives
			.AsNoTracking()
			.Where(item => candidateDates.Contains(item.Date))
			.OrderBy(item => item.Date)
			.ThenBy(item => item.StartTime)
			.ToListAsync(cancellationToken);

		// Super-day attentives (week/month/year occurrences) occupy their whole period, so an instance whose
		// period covers this window collides even when its start date is well before it — this is how a
		// single attentive collides with multiple Polaris cycles.
		var minCandidate = candidateDates[0];
		var maxCandidateExclusive = candidateDates[^1].AddDays(1);
		var attentives = await context.Attentives
			.AsNoTracking()
			.Where(item => item.PolarisCycleId == null
				&& (candidateDates.Contains(item.Date)
					|| (item.PeriodEndDate != null && item.Date < maxCandidateExclusive && item.PeriodEndDate > minCandidate)))
			.OrderBy(item => item.Date)
			.ThenBy(item => item.Time)
			.ToListAsync(cancellationToken);

		return new PolarisCycleInclusions(
			eventives.Where(item => InclusionWindow.Intersects(item.Date, item.StartTime, item.EndTime, windowStart, windowEnd)).ToList(),
			attentives.Where(item => InclusionWindow.AttentiveIntersects(item, windowStart, windowEnd)).ToList());
	}

	/// <inheritdoc />
	public async Task<PolarisAgenda> GetAgendaAsync(CancellationToken cancellationToken = default)
	{
		var today = DateOnly.FromDateTime(DateTime.Today);
		var horizon = today.AddDays(7);
		// "Requiring attention" spans the next 24h, so at day granularity that is today plus tomorrow, alongside
		// anything overdue. This mirrors what the rolling materialization pass writes for the same window.
		var attentiveThrough = today.AddDays(1);
		var now = DateTimeOffset.UtcNow;
		var resolvedSince = now.AddHours(-1);

		// Requiring attention: unbound, still pending, and due within the next 24h or overdue (same-day/24h and
		// previous unattended), plus unbound attentives completed in the past hour. SQLite
		// does not provide reliable translated comparison semantics for DateTimeOffset, so that rolling comparison
		// is applied after materialization. Including the decree pulls its directive through the auto-include, so
		// each item can show its relevant lunar directive.
		var attentiveCandidates = await context.Attentives
			.AsNoTracking()
			.Include(item => item.Decree)
			.Where(item => item.PolarisCycleId == null
				&& ((item.Resolution == AttentiveResolution.Pending && item.Date <= attentiveThrough)
					|| item.ResolvedOn != null))
			.ToListAsync(cancellationToken);

		var attentives = attentiveCandidates
			.Where(item => (item.PolarisCycleId == null
					&& item.Resolution == AttentiveResolution.Pending
					&& item.Date <= attentiveThrough)
				|| (item.PolarisCycleId == null && item.ResolvedOn >= resolvedSince && item.ResolvedOn <= now))
			.OrderBy(item => item.Date)
			.ThenBy(item => item.Time)
			.ToList();

		// Upcoming eventives within the horizon that have not yet resolved. Fate and objective are included so
		// the occurrence can name its owner and surface that owner's directive.
		var eventives = await context.Eventives
			.AsNoTracking()
			.Include(item => item.Fate)
			.Include(item => item.Objective)
			.Where(item => item.Resolution == EventiveResolution.Pending
				&& item.Date >= today
				&& item.Date <= horizon)
			.OrderBy(item => item.Date)
			.ThenBy(item => item.StartTime)
			.ToListAsync(cancellationToken);

		return new PolarisAgenda(attentives, eventives);
	}

	/// <inheritdoc />
	public async Task<Attentive> AddDecreeAttentiveAsync(PolarisAttentiveAdd request, string? polarisCycleId = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentException.ThrowIfNullOrWhiteSpace(request.DecreeId);

		var cycle = await ResolveCycleForMutationAsync(polarisCycleId, requireTodayFallback: false, cancellationToken)
			?? throw new InvalidOperationException("No Polaris cycle is available for attentive planning.");
		var decree = await context.Decrees.AsNoTracking().FirstOrDefaultAsync(item => item.Id == request.DecreeId, cancellationToken)
			?? throw new InvalidOperationException($"Decree '{request.DecreeId}' was not found.");
		if (decree.Status != DecreeStatus.Active)
		{
			throw new InvalidOperationException($"Decree '{decree.Id}' is {decree.Status} and cannot be added to a Polaris cycle.");
		}

		var attentive = new Attentive
		{
			DecreeId = decree.Id,
			PolarisCycleId = cycle.Id,
			Date = request.Date ?? ResolveCycleDate(cycle),
			Time = request.Time,
			Estimation = request.Estimation ?? decree.DefaultLength,
			Minimum = request.Minimum,
			Maximum = request.Maximum,
		};
		attentive.Normalize();

		context.Attentives.Add(attentive);
		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"polaris.add-attentive",
			subjectType: nameof(Attentive),
			subjectId: attentive.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
			details: new { cycleId = cycle.Id, decreeId = decree.Id, date = attentive.Date.ToString("yyyy-MM-dd") },
			cancellationToken: cancellationToken);
		return attentive;
	}

	private static DateOnly ResolveCycleDate(PolarisCycle cycle)
	{
		return cycle.StartTime is not null
			? DateOnly.FromDateTime(cycle.StartTime.Value.LocalDateTime)
			: DateOnly.FromDateTime(DateTime.Today);
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

		if (update.Time.IsSet)
		{
			reflective.Time = update.Time.Value;
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