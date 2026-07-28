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
	PlaintorchOrbitService orbitService,
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
		await MaterializeProximityEventivesAsync(cycle, cancellationToken);
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
		await MaterializeProximityEventivesAsync(cycle, cancellationToken);
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
			Estimation = plan.Estimation,
			Minimum = plan.Minimum,
			Maximum = plan.Maximum,
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

		if (!string.IsNullOrWhiteSpace(update.ObjectiveId))
		{
			executive.ObjectiveId = update.ObjectiveId;
		}

		if (update.ClearObjective)
		{
			executive.ObjectiveId = null;
		}

		if (update.Estimation is not null)
		{
			executive.Estimation = update.Estimation;
		}

		if (update.ClearEstimation)
		{
			executive.Estimation = null;
		}

		if (update.Minimum is not null)
		{
			executive.Minimum = update.Minimum;
		}

		if (update.ClearMinimum)
		{
			executive.Minimum = null;
		}

		if (update.Maximum is not null)
		{
			executive.Maximum = update.Maximum;
		}

		if (update.ClearMaximum)
		{
			executive.Maximum = null;
		}

		if (update.Elapsed is not null)
		{
			executive.Elapsed = update.Elapsed.Value;
		}

		if (update.AffinityTimeframeId is not null)
		{
			var timeframeExists = await context.Timeframes.AnyAsync(item => item.Id == update.AffinityTimeframeId.Value, cancellationToken);
			if (!timeframeExists)
			{
				throw new InvalidOperationException($"Timeframe '{update.AffinityTimeframeId}' was not found.");
			}

			executive.AffinityTimeframeId = update.AffinityTimeframeId;
		}

		if (update.ClearAffinityTimeframe)
		{
			executive.AffinityTimeframeId = null;
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

		var (windowStart, windowEnd) = ResolveInclusionWindow(cycle);
		var candidateDates = EnumerateWindowDates(windowStart, windowEnd);

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
			eventives.Where(item => IntersectsWindow(item.Date, item.StartTime, item.EndTime, windowStart, windowEnd)).ToList(),
			attentives.Where(item => AttentiveIntersectsWindow(item, windowStart, windowEnd)).ToList());
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

	/// <summary>
	/// Materializes backlog instances through proximity for a beginning cycle (PEP100):
	/// dated and orbit-scheduled fates colliding within 24h of the starting point ensure their eventives,
	/// objective due dates ensure their eventives, orbit-scheduled decrees ensure their unbound attentives,
	/// and lunar-hierarchy reflect-decrees whose orbit matches the cycle's start day generate cycle-bound
	/// reflectives. Orbit resolution here SEEKS (advances the persisted schedule state); instance identity is
	/// the occurrence date, so previously interacted future instances are recognized, not duplicated.
	/// </summary>
	private async Task MaterializeProximityEventivesAsync(PolarisCycle cycle, CancellationToken cancellationToken)
	{
		if (cycle.StartTime is null)
		{
			return;
		}

		var (windowStart, windowEnd) = ResolveInclusionWindow(cycle);
		var candidateDates = EnumerateWindowDates(windowStart, windowEnd);
		var startDay = DateOnly.FromDateTime(windowStart);
		var windowEndDayExclusive = DateOnly.FromDateTime(windowEnd).AddDays(windowEnd.TimeOfDay > TimeSpan.Zero ? 1 : 0);
		var created = 0;

		// Dated fates: collide by their explicit time specification.
		var datedFates = await context.Fates
			.AsNoTracking()
			.IgnoreAutoIncludes()
			.Where(fate => fate.Status == FateStatus.Active && fate.Date != null && candidateDates.Contains(fate.Date.Value))
			.ToListAsync(cancellationToken);

		foreach (var fate in datedFates)
		{
			if (!IntersectsWindow(fate.Date!.Value, fate.StartTime, fate.EndTime, windowStart, windowEnd))
			{
				continue;
			}

			created += await EnsureFateEventiveAsync(fate, fate.Date.Value, cancellationToken) ? 1 : 0;
		}

		// Orbit-scheduled fates: seek their (Gregorian-calendar) schedules through the window end, catching up
		// on anything pending since the last seek. Span-format orbits carry the eventive length themselves.
		var orbitFates = await context.Fates
			.IgnoreAutoIncludes()
			.Where(fate => fate.Status == FateStatus.Active && fate.Orbit != null)
			.ToListAsync(cancellationToken);

		foreach (var fate in orbitFates)
		{
			foreach (var occurrence in await orbitService.SeekOccurrencesAsync(fate, fate.Orbit!, windowEndDayExclusive, cancellationToken))
			{
				created += await EnsureFateEventiveAsync(fate, occurrence, cancellationToken) ? 1 : 0;
			}
		}

		// Orbit-scheduled decrees resolve on the Pleiadean calendar. Lunar reflect-decrees resolve at day
		// granularity against the cycle's start day and generate cycle-bound reflectives; every other decree
		// materializes unbound attentives — timed for sub-day granularities, period-spanning for super-day
		// granularities (so multiple cycles can collide with one instance).
		var orbitDecrees = await context.Decrees
			.IgnoreAutoIncludes()
			.Where(decree => decree.Status == DecreeStatus.Active && decree.Orbit != null)
			.ToListAsync(cancellationToken);

		foreach (var decree in orbitDecrees)
		{
			var reflects = decree.Reflect && await orbitService.IsInLunarHierarchyAsync(decree.DirectiveId, cancellationToken);
			if (reflects)
			{
				var occurrences = await orbitService.SeekOccurrencesAsync(decree, decree.Orbit!, startDay.AddDays(1), cancellationToken);
				if (occurrences.Any(occurrence => occurrence.Date <= startDay && startDay < occurrence.PeriodEndExclusive))
				{
					var reflectiveExists = await context.Set<Reflective>()
						.AnyAsync(item => item.PolarisCycleId == cycle.Id && item.DecreeId == decree.Id, cancellationToken);
					if (!reflectiveExists)
					{
						context.Add(new Reflective
						{
							Description = decree.Title,
							PolarisCycleId = cycle.Id,
							DecreeId = decree.Id,
							Executed = false,
						});
						created++;
					}
				}

				continue;
			}

			foreach (var occurrence in await orbitService.SeekOccurrencesAsync(decree, decree.Orbit!, windowEndDayExclusive, cancellationToken))
			{
				var attentiveExists = await context.Attentives.AnyAsync(
					item => item.DecreeId == decree.Id
						&& item.Date == occurrence.Date
						&& item.Time == occurrence.StartTime
						&& item.PolarisCycleId == null,
					cancellationToken);
				if (attentiveExists)
				{
					continue;
				}

				var attentive = new Attentive
				{
					DecreeId = decree.Id,
					Date = occurrence.Date,
					Time = occurrence.StartTime,
					PeriodEndDate = occurrence.PeriodEndExclusive > occurrence.Date.AddDays(1)
						? occurrence.PeriodEndExclusive
						: null,
					Estimation = decree.DefaultLength,
				};
				attentive.Normalize();
				context.Attentives.Add(attentive);
				created++;
			}
		}

		var dueObjectives = await context.Objectives
			.AsNoTracking()
			.IgnoreAutoIncludes()
			.Where(objective => objective.Due != null
				&& candidateDates.Contains(objective.Due.Value)
				&& objective.Status != ObjectiveStatus.Done
				&& objective.Status != ObjectiveStatus.Archived
				&& objective.Status != ObjectiveStatus.Failed)
			.ToListAsync(cancellationToken);

		foreach (var objective in dueObjectives)
		{
			var exists = await context.Eventives.AnyAsync(item => item.ObjectiveId == objective.Id && item.Date == objective.Due!.Value, cancellationToken);
			if (exists)
			{
				continue;
			}

			context.Eventives.Add(new Eventive
			{
				ObjectiveId = objective.Id,
				Date = objective.Due!.Value,
			});
			created++;
		}

		// Advanced orbit states persist even when no new instances were created.
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
	}

	/// <summary>
	/// Ensures a dated fate's eventive exists for an occurrence day, returning whether one was created.
	/// </summary>
	private Task<bool> EnsureFateEventiveAsync(Fate fate, DateOnly day, CancellationToken cancellationToken)
	{
		return EnsureFateEventiveCoreAsync(fate, day, fate.StartTime, fate.EndTime, fate.ResolveEventiveDuration(), cancellationToken);
	}

	/// <summary>
	/// Ensures a fate's eventive exists for an orbit occurrence, returning whether one was created.
	/// Span occurrences carry their own start/end/length; granular occurrences fall back to the fate's
	/// time specification.
	/// </summary>
	private Task<bool> EnsureFateEventiveAsync(Fate fate, Pleiades.Orbits.OrbitOccurrenceInstance occurrence, CancellationToken cancellationToken)
	{
		return EnsureFateEventiveCoreAsync(
			fate,
			occurrence.Date,
			occurrence.StartTime ?? fate.StartTime,
			occurrence.EndTime ?? (occurrence.StartTime is null ? fate.EndTime : null),
			occurrence.DurationMinutes ?? fate.ResolveEventiveDuration(),
			cancellationToken);
	}

	private async Task<bool> EnsureFateEventiveCoreAsync(Fate fate, DateOnly day, TimeOnly? startTime, TimeOnly? endTime, int? estimation, CancellationToken cancellationToken)
	{
		var exists = await context.Eventives.AnyAsync(
			item => item.FateId == fate.Id && item.Date == day && item.StartTime == startTime,
			cancellationToken);
		if (exists)
		{
			return false;
		}

		var eventive = new Eventive
		{
			FateId = fate.Id,
			Date = day,
			StartTime = startTime,
			EndTime = endTime,
			Estimation = estimation,
		};
		eventive.Normalize();
		context.Eventives.Add(eventive);
		return true;
	}

	/// <summary>
	/// Resolves the 24h inclusion window that follows the cycle's starting point.
	/// </summary>
	private static (DateTime Start, DateTime End) ResolveInclusionWindow(PolarisCycle cycle)
	{
		var start = cycle.StartTime!.Value.LocalDateTime;
		return (start, start.AddHours(24));
	}

	/// <summary>
	/// Enumerates the occurrence dates that can possibly intersect the window (used to prefilter in SQL).
	/// </summary>
	private static List<DateOnly> EnumerateWindowDates(DateTime windowStart, DateTime windowEnd)
	{
		var dates = new List<DateOnly>();
		for (var date = DateOnly.FromDateTime(windowStart); date <= DateOnly.FromDateTime(windowEnd); date = date.AddDays(1))
		{
			dates.Add(date);
		}

		return dates;
	}

	/// <summary>
	/// Determines whether an occurrence intersects the inclusion window. Items without times occupy their
	/// whole day; timed items occupy their start/end span.
	/// </summary>
	private static bool IntersectsWindow(DateOnly date, TimeOnly? startTime, TimeOnly? endTime, DateTime windowStart, DateTime windowEnd)
	{
		var occurrenceStart = startTime is null
			? date.ToDateTime(TimeOnly.MinValue)
			: date.ToDateTime(startTime.Value);
		var occurrenceEnd = startTime is null
			? date.AddDays(1).ToDateTime(TimeOnly.MinValue)
			: date.ToDateTime(endTime ?? startTime.Value);

		return occurrenceEnd >= windowStart && occurrenceStart < windowEnd;
	}

	/// <summary>
	/// Determines whether an attentive collides with the inclusion window: period attentives occupy
	/// [Date, PeriodEndDate), timed attentives their instant, and all-day attentives their whole day.
	/// </summary>
	private static bool AttentiveIntersectsWindow(Attentive attentive, DateTime windowStart, DateTime windowEnd)
	{
		if (attentive.PeriodEndDate is { } periodEnd)
		{
			var occurrenceStart = attentive.Date.ToDateTime(TimeOnly.MinValue);
			var occurrenceEnd = periodEnd.ToDateTime(TimeOnly.MinValue);
			return occurrenceEnd >= windowStart && occurrenceStart < windowEnd;
		}

		return IntersectsWindow(attentive.Date, attentive.Time, attentive.Time, windowStart, windowEnd);
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

		if (update.Time is not null)
		{
			reflective.Time = update.Time;
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