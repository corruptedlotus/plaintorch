using Microsoft.EntityFrameworkCore;
using Pleiades.Orbits;
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
	VaultWriteQueue writeQueue,
	ProximityMaterializationService materializationService,
	TimeframeAffinityResolver affinityResolver,
	IServiceScopeFactory scopeFactory,
	AgendaProjectionService projectionService,
	VaultAuditLogService auditLogService,
	ILogger<PolarisCycleApiService> logger) : IPolarisCycleApi
{
	/// <inheritdoc />
	public async Task<PolarisCycle> PlanAsync(DateOnly forecastReference, int daysAhead, string? body = null, CancellationToken cancellationToken = default)
	{
		var targetDate = forecastReference.AddDays(daysAhead);
		var id = puckCreationService.CreateIdFor<PolarisCycle>(systemSegments: [new PuckSegmentInput(Date: targetDate)]);
		var cycle = lifecycle.PlanForecast(forecastReference, daysAhead, id);

		context.PolarisCycles.Add(cycle);
		await writeQueue.WriteAsync(cycle, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "polaris.plan", subject: cycle, cancellationToken: cancellationToken);
		return cycle;
	}

	/// <inheritdoc />
	public async Task<PolarisCycle> BeginAsync(string? polarisCycleId = null, DateTimeOffset? startTime = null, CancellationToken cancellationToken = default)
	{
		var cycle = await ResolveCycleForMutationAsync(polarisCycleId, cancellationToken)
			?? throw new InvalidOperationException("No Polaris cycle is available to begin.");

		return await ActivateAsync(cycle, startTime ?? DateTimeOffset.UtcNow, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<PolarisCycle> StartNewAsync(DateTimeOffset? startTime = null, string? body = null, CancellationToken cancellationToken = default)
	{
		// "Start new" is really "ensure today's cycle is active": a cycle planned for the day is activated in place
		// rather than duplicated (its id IS the day, so a second would collide), and one is created only when none
		// exists. All activation routes through the same path (Fix 1).
		return await EnsureActiveCycleAsync(startTime, cancellationToken);
	}

	/// <summary>
	/// The single activation path — begin, start-new, and the auto-start when adding to the current cycle all route
	/// here. Begins the cycle unless it is already active, materializes its proximity items, persists, and finally warms
	/// its timeframe candidates best effort (PEP100 patch 2).
	/// </summary>
	private async Task<PolarisCycle> ActivateAsync(PolarisCycle cycle, DateTimeOffset startTime, CancellationToken cancellationToken)
	{
		if (cycle.StartTime is not null && cycle.EndTime is null)
		{
			return cycle;
		}

		var previous = Clone(cycle);
		var started = lifecycle.Start(cycle, startTime);
		ApplyCycle(cycle, started);
		await context.SaveChangesAsync(cancellationToken);
		await materializationService.MaterializeForCycleAsync(cycle, cancellationToken);
		await writeQueue.WriteAsync(cycle, previous, cancellationToken);
		await auditLogService.WriteAsync("api", "polaris.begin", subject: cycle, cancellationToken: cancellationToken);
		await WarmCandidatesAsync(cycle, cancellationToken);
		return cycle;
	}

	/// <summary>
	/// Warms a freshly begun cycle's timeframe candidates (PEP100 patch 2), best effort: the begin is already committed
	/// and written by now, and the candidate listing recomputes lazily on a miss, so a failed warm is logged and never
	/// fails the begin.
	/// </summary>
	/// <remarks>
	/// The warm runs in a child scope, as the vault-activation warm does: this request scope may already have read the
	/// default-calendar preference (materialization resolves calendars), and its options snapshot would keep that value
	/// even when a calendar change completed in between, storing candidates read on the old calendar as current.
	/// </remarks>
	private async Task WarmCandidatesAsync(PolarisCycle cycle, CancellationToken cancellationToken)
	{
		// The lifecycle keeps an existing end time, so re-beginning an ended cycle leaves it ended; only a cycle that is
		// now strictly active has candidates worth warming.
		if (cycle.StartTime is null || cycle.EndTime is not null)
		{
			return;
		}

		try
		{
			using var scope = scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<TimeframeCandidateService>().RefreshAsync(cycle, cancellationToken);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			logger.LogWarning(exception, "PLAINTORCH could not warm the timeframe candidates of Polaris cycle {CycleId}; they will be computed on first read.", cycle.Id);
		}
	}

	/// <summary>
	/// Gets the active Polaris cycle, activating or creating one for the day when none is active. A cycle already
	/// planned for the day is activated in place (never duplicated, since its id is the day); a day with no cycle yet
	/// gets a fresh one created and activated. This is the shared primitive behind start-new and add-to-current.
	/// </summary>
	private async Task<PolarisCycle> EnsureActiveCycleAsync(DateTimeOffset? startTime, CancellationToken cancellationToken)
	{
		var resolvedStartTime = startTime ?? DateTimeOffset.UtcNow;

		var active = await context.PolarisCycles
			.FirstOrDefaultAsync(item => item.StartTime != null && item.EndTime == null, cancellationToken);
		if (active is not null)
		{
			return active;
		}

		var targetDate = DateOnly.FromDateTime(resolvedStartTime.LocalDateTime);
		// Compose (do NOT mint) the deterministic day id to look up an existing cycle; minting here would register a
		// duplicate PUCK registry entry for a date whose cycle already exists.
		var dayId = puckCreationService.ComposeIdFor<PolarisCycle>(systemSegments: [new PuckSegmentInput(Date: targetDate)]);

		var dayCycle = await context.PolarisCycles.FirstOrDefaultAsync(item => item.Id == dayId, cancellationToken);
		if (dayCycle is not null)
		{
			// Planned for today → activate in place; already started (and now ended) → leave it as it is.
			return dayCycle.StartTime is null
				? await ActivateAsync(dayCycle, resolvedStartTime, cancellationToken)
				: dayCycle;
		}

		var created = new PolarisCycle
		{
			// No cycle exists for the day, so mint (register) the id now.
			Id = puckCreationService.CreateIdFor<PolarisCycle>(systemSegments: [new PuckSegmentInput(Date: targetDate)]),
			Title = PlaintorchDefaultTitleFactory.CreatePolarisTitle(targetDate),
		};
		context.PolarisCycles.Add(created);
		await context.SaveChangesAsync(cancellationToken);
		return await ActivateAsync(created, resolvedStartTime, cancellationToken);
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

		await writeQueue.WriteAsync(cycle, previous, cancellationToken);
		await auditLogService.WriteAsync("api", "polaris.update", subject: cycle, cancellationToken: cancellationToken);
		return cycle;
	}

	/// <inheritdoc />
	public async Task<PolarisCycle> EndAsync(string? polarisCycleId = null, DateTimeOffset? endTime = null, CancellationToken cancellationToken = default)
	{
		var cycle = await ResolveCycleForMutationAsync(polarisCycleId, cancellationToken)
			?? throw new InvalidOperationException("No Polaris cycle is available to end.");

		var previous = Clone(cycle);
		var finished = lifecycle.Finish(cycle, endTime ?? DateTimeOffset.UtcNow);
		ApplyCycle(cycle, finished);
		await writeQueue.WriteAsync(cycle, previous, cancellationToken);
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
				.ThenInclude(executive => executive.Incentive)
					.ThenInclude(incentive => incentive!.Directive)
			.Include(item => item.Executives)
				.ThenInclude(executive => executive.AffinityTimeframe)
			.Include(item => item.Reflectives)
				.ThenInclude(reflective => reflective.Decree)
					.ThenInclude(decree => decree!.Directive)
			.FirstOrDefaultAsync(item => item.Id == targetId, cancellationToken);

		return cycle;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<PolarisCycle>> ListForecastsAsync(CancellationToken cancellationToken = default)
	{
		return await context.PolarisCycles
			.AsNoTracking()
			.Include(cycle => cycle.Executives)
				.ThenInclude(executive => executive.Incentive)
			.Where(cycle => cycle.Forecast != null && cycle.StartTime == null)
			.OrderBy(cycle => cycle.Id)
			.ToListAsync(cancellationToken);
	}

	/// <inheritdoc />
	public async Task<PolarisExecutivePlanResult> PlanExecutiveAsync(PolarisExecutivePlan plan, string? polarisCycleId = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(plan);
		// An explicit affinity is validated before a cycle is started or an objective created, so a refused request
		// leaves nothing behind; Auto can only resolve once the objective exists.
		var explicitAffinity = plan.AffinityTimeframeId.IsSet
			? await ResolveCreationAffinityAsync(plan.AffinityTimeframeId, null, null, cancellationToken)
			: null;
		var cycle = await ResolveOrStartCurrentAsync(polarisCycleId, cancellationToken);

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
				objective = await CreateObjectiveAsync(plan.Title!, null, plan.OnrushSprintId, plan.College, plan.CelestronValue, cancellationToken);
				executiveTitle = plan.ExecutiveTitle ?? objective.Title;
				break;

			case PolarisExecutivePlanningMode.FromDirective:
				ArgumentException.ThrowIfNullOrWhiteSpace(plan.DirectiveId);
				ArgumentException.ThrowIfNullOrWhiteSpace(plan.Title);
				objective = await CreateObjectiveAsync(plan.Title!, plan.DirectiveId, plan.OnrushSprintId, plan.College, plan.CelestronValue, cancellationToken);
				executiveTitle = plan.ExecutiveTitle ?? objective.Title;
				break;

			case PolarisExecutivePlanningMode.FromObjective:
				ArgumentException.ThrowIfNullOrWhiteSpace(plan.ObjectiveId);
				objective = await context.Objectives.FirstOrDefaultAsync(item => item.Id == plan.ObjectiveId, cancellationToken)
					?? throw new InvalidOperationException($"Objective '{plan.ObjectiveId}' was not found.");
				await EnsureIncentiveNotInCycleAsync(cycle.Id, objective.Id, cancellationToken);
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
			IncentiveId = objective?.Id,
			Executed = false,
			Estimation = plan.Estimation,
			Minimum = plan.Minimum,
			Maximum = plan.Maximum,
			// Auto-inclusion (PEP100 patch 2): unless the request names an affinity (or explicitly none), an executive
			// built from an objective takes the objective's nearest directive availability, else its college. One-shot
			// executives carry no objective and so no auto-affinity.
			AffinityTimeframeId = plan.AffinityTimeframeId.IsSet
				? explicitAffinity
				: await ResolveCreationAffinityAsync(plan.AffinityTimeframeId, objective?.DirectiveId, objective?.College, cancellationToken),
		};
		executive.NormalizeTimeAllocations();

		context.Add(executive);
		await context.SaveChangesAsync(cancellationToken);
		await LoadAffinityAsync(executive, cancellationToken);
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

		// The executive's incentive can be reassigned but never cleared — an executive without an incentive has
		// nothing to work at.
		if (!string.IsNullOrWhiteSpace(update.ObjectiveId) && update.ObjectiveId != executive.IncentiveId)
		{
			await EnsureIncentiveNotInCycleAsync(executive.PolarisCycleId, update.ObjectiveId, cancellationToken);
			executive.IncentiveId = update.ObjectiveId;
		}

		// An executive can be relocated to another Polaris cycle (the successor to moving a Polaris-bound
		// attentive, PEP111), refused when the target cycle already holds the same incentive.
		if (!string.IsNullOrWhiteSpace(update.MoveToPolarisCycleId) && update.MoveToPolarisCycleId != executive.PolarisCycleId)
		{
			if (!await context.PolarisCycles.AnyAsync(item => item.Id == update.MoveToPolarisCycleId, cancellationToken))
			{
				throw new InvalidOperationException($"Polaris cycle '{update.MoveToPolarisCycleId}' was not found.");
			}

			if (!string.IsNullOrWhiteSpace(executive.IncentiveId))
			{
				await EnsureIncentiveNotInCycleAsync(update.MoveToPolarisCycleId, executive.IncentiveId, cancellationToken);
			}

			executive.PolarisCycleId = update.MoveToPolarisCycleId;
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
			details: new { incentiveId = executive.IncentiveId, executed = executive.Executed, estimation = executive.Estimation, minimum = executive.Minimum, maximum = executive.Maximum, elapsed = executive.Elapsed },
			cancellationToken: cancellationToken);
		return executive;
	}

	/// <inheritdoc />
	public async Task<Executive> MoveExecutiveToNextPolarisAsync(long executiveId, CancellationToken cancellationToken = default)
	{
		var executive = await context.Set<Executive>()
			.Include(item => item.PolarisCycle)
			.FirstOrDefaultAsync(item => item.Id == executiveId, cancellationToken)
			?? throw new InvalidOperationException($"Executive '{executiveId}' was not found.");

		var currentDate = ResolveCycleDate(executive.PolarisCycle);
		var next = await EnsureNextForecastCycleAsync(currentDate, cancellationToken);

		// A cycle holds at most one executive per incentive (PEP111).
		if (!string.IsNullOrWhiteSpace(executive.IncentiveId))
		{
			await EnsureIncentiveNotInCycleAsync(next.Id, executive.IncentiveId, cancellationToken);
		}

		// Carry the tracked work forward as the fresh allocation envelope — the day starts from an estimate of
		// what was actually worked so far — and restart tracking from zero.
		var carried = executive.Elapsed;
		executive.PolarisCycleId = next.Id;
		executive.Estimation = carried;
		executive.Minimum = carried;
		executive.Maximum = carried;
		executive.Elapsed = 0;
		executive.NormalizeTimeAllocations();

		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"polaris.move-executive-next",
			subjectType: nameof(Executive),
			subjectId: executive.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
			details: new { toCycleId = next.Id, incentiveId = executive.IncentiveId, carried },
			cancellationToken: cancellationToken);
		return executive;
	}

	/// <summary>
	/// Finds the cycle for the day after <paramref name="currentDate"/>, or plans and persists a forecast one
	/// (a one-day-ahead forecast anchored on <paramref name="currentDate"/>).
	/// </summary>
	private async Task<PolarisCycle> EnsureNextForecastCycleAsync(DateOnly currentDate, CancellationToken cancellationToken)
	{
		var nextDate = currentDate.AddDays(1);
		var existingId = puckCreationService.ComposeIdFor<PolarisCycle>(systemSegments: [new PuckSegmentInput(Date: nextDate)]);
		var existing = await context.PolarisCycles.FirstOrDefaultAsync(item => item.Id == existingId, cancellationToken);
		if (existing is not null)
		{
			return existing;
		}

		var id = puckCreationService.CreateIdFor<PolarisCycle>(systemSegments: [new PuckSegmentInput(Date: nextDate)]);
		var forecast = lifecycle.PlanForecast(currentDate, 1, id);
		context.PolarisCycles.Add(forecast);
		// A Polaris cycle is vault-backed, so its creation goes through the write queue (PEP110), like PlanAsync.
		await writeQueue.WriteAsync(forecast, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "polaris.plan", subject: forecast, cancellationToken: cancellationToken);
		return forecast;
	}

	/// <summary>
	/// Resolves the calendar day a cycle stands for: its start day when started, its forecast target day when
	/// still a forecast, else today (a defensive fallback for a cycle that is neither).
	/// </summary>
	private static DateOnly ResolveCycleDate(PolarisCycle? cycle)
	{
		if (cycle?.StartTime is { } start)
		{
			return DateOnly.FromDateTime(start.LocalDateTime);
		}

		if (cycle?.Forecast is { } forecast
			&& int.TryParse(forecast.ForecastTarget.Trim().TrimStart('+').TrimEnd('d'), System.Globalization.CultureInfo.InvariantCulture, out var daysAhead))
		{
			return forecast.ForecastReference.AddDays(daysAhead);
		}

		return DateOnly.FromDateTime(DateTime.Today);
	}

	/// <inheritdoc />
	public async Task RemoveExecutiveAsync(long executiveId, CancellationToken cancellationToken = default)
	{
		var executive = await context.Set<Executive>().FirstOrDefaultAsync(item => item.Id == executiveId, cancellationToken)
			?? throw new InvalidOperationException($"Executive '{executiveId}' was not found.");

		// Only the cycle's record goes. The core moves a backlog item's state forward on the strength of cycle
		// participation and never back, so the objective keeps whatever state it has — stepping it out of Polaris is
		// the user's call, not a side effect of tidying a cycle.
		context.Remove(executive);
		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"polaris.remove-executive",
			subjectType: nameof(Executive),
			subjectId: executive.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
			details: new { cycleId = executive.PolarisCycleId, incentiveId = executive.IncentiveId, executed = executive.Executed },
			cancellationToken: cancellationToken);
	}

	/// <inheritdoc />
	public async Task<PolarisCycleInclusions> GetInclusionsAsync(string? polarisCycleId = null, CancellationToken cancellationToken = default)
	{
		var cycle = await ResolveCycleForMutationAsync(polarisCycleId, cancellationToken)
			?? throw new InvalidOperationException("No Polaris cycle is available for inclusion listing.");

		if (cycle.StartTime is null)
		{
			return new PolarisCycleInclusions([], []);
		}

		var (windowStart, windowEnd) = InclusionWindow.Resolve(cycle);

		// Projection ⊔ hardened over the window's days; the intersect filters then trim to the exact 24h span.
		// Super-day attentives whose period covers the window are included by the projection even when their
		// start date is well before it — this is how a single attentive collides with multiple Polaris cycles.
		var projection = await projectionService.ProjectAsync(
			DateOnly.FromDateTime(windowStart),
			DateOnly.FromDateTime(windowEnd),
			cancellationToken);

		return new PolarisCycleInclusions(
			projection.Eventives.Where(item => InclusionWindow.EventiveIntersects(item, windowStart, windowEnd)).ToList(),
			projection.Attentives.Where(item => InclusionWindow.AttentiveIntersects(item, windowStart, windowEnd)).ToList());
	}

	/// <inheritdoc />
	public async Task<PolarisAgenda> GetAgendaAsync(CancellationToken cancellationToken = default)
	{
		var today = DateOnly.FromDateTime(DateTime.Today);
		var todayStart = today.ToDateTime(TimeOnly.MinValue);
		var horizon = today.AddDays(7);
		// "Requiring attention" spans the next 24h, so at day granularity that is today plus tomorrow, alongside
		// anything overdue.
		var attentiveThrough = today.AddDays(1);
		var now = DateTimeOffset.UtcNow;
		var resolvedSince = now.AddHours(-1);

		// Projection ⊔ hardened over the horizon: untouched occurrences are projected live from their schedules,
		// while interacted/resolved ones carry their persisted state.
		var projection = await projectionService.ProjectAsync(today, horizon, cancellationToken);

		// Overdue pending attentives sit before the projected window (a direct query over hardened rows).
		var overdueAttentives = await context.Attentives
			.AsNoTracking()
			.Include(item => item.Decree)
			.Where(item => item.Epoch.Moment < todayStart
				&& item.Resolution == AttentiveResolution.Pending)
			.ToListAsync(cancellationToken);

		// Recently-resolved attentives are retained for an hour regardless of their date, so they are fetched
		// independently of the projection window. SQLite lacks reliable translated DateTimeOffset comparison, so the
		// resolved-since window is applied after materialization.
		var recentlyResolved = await context.Attentives
			.AsNoTracking()
			.Include(item => item.Decree)
			.Where(item => item.ResolvedOn != null)
			.ToListAsync(cancellationToken);

		var attentives = projection.Attentives
			.Concat(overdueAttentives)
			.Where(item => item.Resolution == AttentiveResolution.Pending
				&& item.Epoch.Date <= attentiveThrough)
			.Concat(recentlyResolved.Where(item => item.ResolvedOn >= resolvedSince && item.ResolvedOn <= now))
			.OrderBy(item => item.Epoch.Moment)
			.ToList();

		var eventives = projection.Eventives
			.Where(item => item.Resolution == EventiveResolution.Pending
				&& item.Epoch.Date >= today
				&& item.Epoch.Date <= horizon)
			.OrderBy(item => item.Epoch.Moment)
			.ToList();

		return new PolarisAgenda(attentives, eventives);
	}

	/// <inheritdoc />
	public async Task<Executive> AddDecreeExecutiveAsync(PolarisDecreeAdd request, string? polarisCycleId = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentException.ThrowIfNullOrWhiteSpace(request.DecreeId);

		// An explicit affinity is validated before a cycle is started, so a refused request leaves nothing behind.
		var explicitAffinity = request.AffinityTimeframeId.IsSet
			? await ResolveCreationAffinityAsync(request.AffinityTimeframeId, null, null, cancellationToken)
			: null;
		var cycle = await ResolveOrStartCurrentAsync(polarisCycleId, cancellationToken);
		var decree = await context.Decrees.AsNoTracking().FirstOrDefaultAsync(item => item.Id == request.DecreeId, cancellationToken)
			?? throw new InvalidOperationException($"Decree '{request.DecreeId}' was not found.");
		if (decree.Status != DecreeStatus.Active)
		{
			throw new InvalidOperationException($"Decree '{decree.Id}' is {decree.Status} and cannot be added to a Polaris cycle.");
		}

		// A decree occupies a cycle as an executive, at most once — the cycle is the temporal context (PEP111).
		await EnsureIncentiveNotInCycleAsync(cycle.Id, decree.Id, cancellationToken);

		var executive = new Executive
		{
			PolarisCycleId = cycle.Id,
			IncentiveId = decree.Id,
			Executed = false,
			Estimation = request.Estimation ?? decree.DefaultLength,
			Minimum = request.Minimum,
			Maximum = request.Maximum,
			// An explicit affinity (or explicit none) wins; otherwise the same auto-inclusion an objective-executive
			// gets, from the decree's nearest directive availability, else its college (PEP100 patch 2).
			AffinityTimeframeId = request.AffinityTimeframeId.IsSet
				? explicitAffinity
				: await ResolveCreationAffinityAsync(request.AffinityTimeframeId, decree.DirectiveId, decree.College, cancellationToken),
		};
		executive.NormalizeTimeAllocations();

		context.Add(executive);
		await context.SaveChangesAsync(cancellationToken);
		await LoadAffinityAsync(executive, cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"polaris.add-decree-executive",
			subjectType: nameof(Executive),
			subjectId: executive.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
			details: new { cycleId = cycle.Id, decreeId = decree.Id },
			cancellationToken: cancellationToken);
		return executive;
	}

	/// <summary>
	/// Resolves a new executive's affinity from the tri-state creation request (PEP100 patch 2): an explicit
	/// <see langword="null"/> is no affinity; an explicit id must name an existing timeframe and is used as given; an
	/// unset request is Auto — the combined resolver over the owning incentive's directive and college, or no affinity
	/// when there is no owning incentive (<paramref name="college"/> is <see langword="null"/>, a one-shot executive).
	/// </summary>
	private async Task<long?> ResolveCreationAffinityAsync(Optional<long?> requested, string? directiveId, ObjectiveCollege? college, CancellationToken cancellationToken)
	{
		if (requested.IsSet)
		{
			if (requested.Value is long timeframeId
				&& !await context.Timeframes.AnyAsync(item => item.Id == timeframeId, cancellationToken))
			{
				throw new InvalidOperationException($"Timeframe '{timeframeId}' was not found.");
			}

			return requested.Value;
		}

		return college is { } owningCollege
			? await affinityResolver.ResolveAsync(directiveId, owningCollege, cancellationToken)
			: null;
	}

	/// <summary>
	/// Loads a freshly saved executive's affinity timeframe so the returned executive carries the resolved navigation,
	/// not only its id (PEP100 patch 2).
	/// </summary>
	private async Task LoadAffinityAsync(Executive executive, CancellationToken cancellationToken)
	{
		var reference = context.Entry(executive).Reference(item => item.AffinityTimeframe);
		if (!reference.IsLoaded)
		{
			await reference.LoadAsync(cancellationToken);
		}
	}

	/// <summary>
	/// Enforces that a Polaris cycle holds at most one executive per incentive: planning an incentive already in the
	/// cycle, or reassigning an executive onto one, is refused rather than producing a second instance. The unique
	/// index on <c>(PolarisCycleId, IncentiveId)</c> is the last line of defence; this is the one that speaks.
	/// </summary>
	private async Task EnsureIncentiveNotInCycleAsync(string polarisCycleId, string incentiveId, CancellationToken cancellationToken)
	{
		var alreadyPlanned = await context.Set<Executive>()
			.AnyAsync(item => item.PolarisCycleId == polarisCycleId && item.IncentiveId == incentiveId, cancellationToken);
		if (alreadyPlanned)
		{
			throw new InvalidOperationException($"Incentive '{incentiveId}' is already an executive of Polaris cycle '{polarisCycleId}'.");
		}
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Reflective>> DrawReflectivesAsync(ReflectiveDrawRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (request.Count < 1)
		{
			throw new ArgumentOutOfRangeException(nameof(request), "At least one reflective must be requested.");
		}

		var cycle = await ResolveOrStartCurrentAsync(request.PolarisCycleId, cancellationToken);

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

	/// <summary>
	/// Resolves a cycle to mutate/read WITHOUT creating one: the cycle with the given id, else the active cycle, else
	/// today's cycle (in whatever state). Returns <see langword="null"/> when nothing matches. Callers that should
	/// start a cycle when none is current use <see cref="ResolveOrStartCurrentAsync"/> instead.
	/// </summary>
	private async Task<PolarisCycle?> ResolveCycleForMutationAsync(string? cycleId, CancellationToken cancellationToken)
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

		var todayId = puckCreationService.ComposeIdFor<PolarisCycle>(systemSegments: [new PuckSegmentInput(Date: DateOnly.FromDateTime(DateTime.Today))]);
		return await context.PolarisCycles.FirstOrDefaultAsync(item => item.Id == todayId, cancellationToken);
	}

	/// <summary>
	/// Resolves the target cycle for an add-to-current operation: an explicit id must resolve to an existing cycle,
	/// but "the current cycle" with none active starts one for today and returns it (Fix 2).
	/// </summary>
	private async Task<PolarisCycle> ResolveOrStartCurrentAsync(string? cycleId, CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(cycleId))
		{
			return await context.PolarisCycles.FirstOrDefaultAsync(item => item.Id == cycleId, cancellationToken)
				?? throw new InvalidOperationException($"Polaris cycle '{cycleId}' was not found.");
		}

		return await EnsureActiveCycleAsync(startTime: null, cancellationToken);
	}


	private async Task<Objective> CreateObjectiveAsync(string title, string? directiveId, string? onrushSprintId, ObjectiveCollege? college, int? celestronValue, CancellationToken cancellationToken)
	{
		var objective = new Objective
		{
			Id = puckCreationService.CreateIdFor<Objective>(),
			Title = title,
			DirectiveId = directiveId,
			OnrushSprintId = onrushSprintId,
			College = college ?? ObjectiveCollege.Unspecified,
			CelestronValue = celestronValue ?? 0,
			Status = ObjectiveStatus.Polaris,
		};

		context.Objectives.Add(objective);
		await writeQueue.WriteAsync(objective, cancellationToken: cancellationToken);
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