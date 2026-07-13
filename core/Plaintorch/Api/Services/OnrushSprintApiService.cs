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
/// Implements the onrush sprint-facing PLAINTORCH application API.
/// </summary>
public sealed class OnrushSprintApiService(
	PlainfraContext context,
	PuckCreationService puckCreationService,
	PuckIdService puckIdService,
	PuckTokenizer puckTokenizer,
	PlaintorchStateService stateService,
	PlaintorchMarkdownStorageService markdownFileService,
	VaultTemporalDataService temporalDataService,
	VaultAuditLogService auditLogService) : IOnrushSprintApi
{
	/// <inheritdoc />
	public async Task<OnrushSprint> PlanAsync(OnrushSprintPlan plan, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentException.ThrowIfNullOrWhiteSpace(plan.Title);

		var sprint = new OnrushSprint
		{
			Id = "0",
			Title = plan.Title,
			StartDate = plan.StartDate,
			EndDate = plan.EndDate,
		};

		context.OnrushSprints.Add(sprint);
		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SaveOnrushSprintAsync(sprint, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "onrush.plan", subject: sprint, cancellationToken: cancellationToken);
		return sprint;
	}

	/// <inheritdoc />
	public async Task<OnrushSprint> BeginAsync(string onrushSprintId, DateOnly? startDate = null, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(onrushSprintId);
		var sprint = await context.OnrushSprints
			.Include(item => item.Objectives)
			.FirstOrDefaultAsync(item => item.Id == onrushSprintId, cancellationToken)
			?? throw new InvalidOperationException($"Onrush sprint '{onrushSprintId}' was not found.");

		if (sprint.StartDate is not null && sprint.EndDate is null && sprint.Id != "0")
		{
			return sprint;
		}

		var previous = Clone(sprint);
		var resolvedStartDate = startDate ?? sprint.StartDate ?? DateOnly.FromDateTime(DateTime.Today);

		if (sprint.Id == "0")
		{
			var activatedSprint = new OnrushSprint
			{
				Id = puckCreationService.CreateIdFor<OnrushSprint>(systemSegments: [new PuckSegmentInput(Date: resolvedStartDate)]),
				Title = sprint.Title,
				StartDate = resolvedStartDate,
				EndDate = sprint.EndDate,
			};

			context.OnrushSprints.Add(activatedSprint);
			foreach (var objective in sprint.Objectives)
			{
				objective.OnrushSprintId = activatedSprint.Id;
			}

			context.OnrushSprints.Remove(sprint);
			await context.SaveChangesAsync(cancellationToken);
			await markdownFileService.SaveOnrushSprintAsync(activatedSprint, previous, cancellationToken: cancellationToken);
			await auditLogService.WriteAsync("api", "onrush.begin", subject: activatedSprint, cancellationToken: cancellationToken);
			return activatedSprint;
		}

		sprint.StartDate = resolvedStartDate;
		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SaveOnrushSprintAsync(sprint, previous, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "onrush.begin", subject: sprint, cancellationToken: cancellationToken);
		return sprint;
	}

	/// <inheritdoc />
	public async Task<OnrushSprint> StartNewAsync(DateOnly? startDate = null, CancellationToken cancellationToken = default)
	{
		var resolvedStartDate = startDate ?? DateOnly.FromDateTime(DateTime.Today);
		var sprint = new OnrushSprint
		{
			Id = puckCreationService.CreateIdFor<OnrushSprint>(systemSegments: [new PuckSegmentInput(Date: resolvedStartDate)]),
			Title = PlaintorchDefaultTitleFactory.CreateOnrushTitle(resolvedStartDate),
			StartDate = resolvedStartDate,
		};

		context.OnrushSprints.Add(sprint);
		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SaveOnrushSprintAsync(sprint, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "onrush.start-new", subject: sprint, cancellationToken: cancellationToken);
		return sprint;
	}

	/// <inheritdoc />
	public async Task<OnrushSprint> EndAsync(string onrushSprintId, DateOnly? endDate = null, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(onrushSprintId);
		var sprint = await context.OnrushSprints.FirstOrDefaultAsync(item => item.Id == onrushSprintId, cancellationToken)
			?? throw new InvalidOperationException($"Onrush sprint '{onrushSprintId}' was not found.");

		var previous = Clone(sprint);
		sprint.EndDate = endDate ?? DateOnly.FromDateTime(DateTime.Today);
		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SaveOnrushSprintAsync(sprint, previous, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "onrush.end", subject: sprint, cancellationToken: cancellationToken);
		return sprint;
	}

	/// <inheritdoc />
	public async Task<OnrushSprint?> GetAsync(string? onrushSprintId = null, CancellationToken cancellationToken = default)
	{
		if (!string.IsNullOrWhiteSpace(onrushSprintId))
		{
			return await context.OnrushSprints
				.AsNoTracking()
				.Include(sprint => sprint.Objectives)
				.Include(sprint => sprint.ExecutiveOrders)
				.FirstOrDefaultAsync(sprint => sprint.Id == onrushSprintId, cancellationToken);
		}

		var activeSprint = await stateService.GetActiveOnrushSprintAsync(cancellationToken);
		if (activeSprint is null)
		{
			return null;
		}

		return await context.OnrushSprints
			.AsNoTracking()
			.Include(sprint => sprint.Objectives)
			.Include(sprint => sprint.ExecutiveOrders)
			.FirstOrDefaultAsync(sprint => sprint.Id == activeSprint.Id, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<OnrushSprint?> GetPlanningAsync(CancellationToken cancellationToken = default)
	{
		var planningSprint = await stateService.GetPlanningOnrushSprintAsync(cancellationToken);
		if (planningSprint is null)
		{
			return null;
		}

		return await context.OnrushSprints
			.AsNoTracking()
			.Include(sprint => sprint.Objectives)
			.Include(sprint => sprint.ExecutiveOrders)
			.FirstOrDefaultAsync(sprint => sprint.Id == planningSprint.Id, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<OnrushSprint>> GetAvailableAsync(CancellationToken cancellationToken = default)
	{
		var active = await GetAsync(null, cancellationToken);
		var planning = await GetPlanningAsync(cancellationToken);

		var available = new List<OnrushSprint>();
		if (active is not null)
		{
			available.Add(active);
		}

		if (planning is not null && !available.Any(item => string.Equals(item.Id, planning.Id, StringComparison.OrdinalIgnoreCase)))
		{
			available.Add(planning);
		}

		return available;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<OnrushSprint>> ListAsync(CancellationToken cancellationToken = default)
	{
		return await context.OnrushSprints
			.AsNoTracking()
			.OrderByDescending(sprint => sprint.StartDate)
			.ThenByDescending(sprint => sprint.Id)
			.ToListAsync(cancellationToken);
	}

	/// <inheritdoc />
	public async Task<OnrushSprint> UpdateAsync(string onrushSprintId, OnrushSprintUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(onrushSprintId);
		ArgumentNullException.ThrowIfNull(update);

		var sprint = await context.OnrushSprints.FirstOrDefaultAsync(item => item.Id == onrushSprintId, cancellationToken)
			?? throw new InvalidOperationException($"Onrush sprint '{onrushSprintId}' was not found.");
		var previous = Clone(sprint);

		if (!string.IsNullOrWhiteSpace(update.Title))
		{
			sprint.Title = update.Title;
		}

		if (update.StartDate is not null)
		{
			sprint.StartDate = update.StartDate;
		}

		if (update.EndDate is not null)
		{
			sprint.EndDate = update.EndDate;
		}

		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SaveOnrushSprintAsync(sprint, previous, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "onrush.update", subject: sprint, cancellationToken: cancellationToken);
		return sprint;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Objective>> AssignAllOnrushStateObjectivesToSelfAsync(string onrushSprintId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(onrushSprintId);
		var sprintExists = await context.OnrushSprints.AnyAsync(sprint => sprint.Id == onrushSprintId, cancellationToken);
		if (!sprintExists)
		{
			throw new InvalidOperationException($"Onrush sprint '{onrushSprintId}' was not found.");
		}

		var objectives = await context.Objectives
			.Where(objective => objective.Status == ObjectiveStatus.Onrush)
			.ToListAsync(cancellationToken);

		foreach (var objective in objectives)
		{
			objective.OnrushSprintId = onrushSprintId;
		}

		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"onrush.assign-onrush-state-objectives",
			subjectType: nameof(OnrushSprint),
			subjectId: onrushSprintId,
			details: new { objectiveCount = objectives.Count },
			cancellationToken: cancellationToken);
		return objectives;
	}

	/// <inheritdoc />
	public async Task<ExecutiveOrder> IssueExecutiveOrderAsync(string onrushSprintId, ExecutiveOrderPlan plan, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(onrushSprintId);
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentException.ThrowIfNullOrWhiteSpace(plan.Title);
		ValidateEffectiveWindow(plan.EffectiveFrom, plan.EffectiveUntil);

		var sprint = await context.OnrushSprints.FirstOrDefaultAsync(item => item.Id == onrushSprintId, cancellationToken)
			?? throw new InvalidOperationException($"Onrush sprint '{onrushSprintId}' was not found.");
		if (sprint.Id == "0")
		{
			throw new InvalidOperationException("Executive orders cannot be issued against the in-planning placeholder sprint because its final PUCK identity is not assigned yet; begin the sprint first.");
		}

		var order = new ExecutiveOrder
		{
			Id = puckIdService.GenerateIdFor<ExecutiveOrder>([new PuckSegmentInput(Numerator: ResolveOnrushNumericPart(sprint.Id)), new PuckSegmentInput()]),
			Title = plan.Title,
			OnrushSprintId = sprint.Id,
			Summary = plan.Summary,
			EffectiveFrom = plan.EffectiveFrom,
			EffectiveUntil = plan.EffectiveUntil,
		};

		context.ExecutiveOrders.Add(order);
		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SaveExecutiveOrderAsync(order, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "onrush.issue-order", subject: order, details: new { onrushSprintId = sprint.Id }, cancellationToken: cancellationToken);
		return order;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<ExecutiveOrder>> ListExecutiveOrdersAsync(string onrushSprintId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(onrushSprintId);
		return await context.ExecutiveOrders
			.AsNoTracking()
			.Where(order => order.OnrushSprintId == onrushSprintId)
			.OrderBy(order => order.Id)
			.ToListAsync(cancellationToken);
	}

	/// <inheritdoc />
	public async Task<ExecutiveOrder> UpdateExecutiveOrderAsync(string executiveOrderId, ExecutiveOrderUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(executiveOrderId);
		ArgumentNullException.ThrowIfNull(update);

		var order = await context.ExecutiveOrders.FirstOrDefaultAsync(item => item.Id == executiveOrderId, cancellationToken)
			?? throw new InvalidOperationException($"Executive order '{executiveOrderId}' was not found.");
		var previous = Clone(order);

		if (!string.IsNullOrWhiteSpace(update.Title))
		{
			order.Title = update.Title;
		}

		if (update.Summary is not null)
		{
			order.Summary = update.Summary;
		}

		if (update.EffectiveFrom is not null)
		{
			order.EffectiveFrom = update.EffectiveFrom;
		}

		if (update.EffectiveUntil is not null)
		{
			order.EffectiveUntil = update.EffectiveUntil;
		}

		ValidateEffectiveWindow(order.EffectiveFrom, order.EffectiveUntil);
		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SaveExecutiveOrderAsync(order, previous, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "onrush.update-order", subject: order, cancellationToken: cancellationToken);
		return order;
	}

	/// <inheritdoc />
	public async Task DeleteExecutiveOrderAsync(string executiveOrderId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(executiveOrderId);
		var order = await context.ExecutiveOrders.FirstOrDefaultAsync(item => item.Id == executiveOrderId, cancellationToken)
			?? throw new InvalidOperationException($"Executive order '{executiveOrderId}' was not found.");

		var graveyardEntry = await temporalDataService.ArchiveEntityAsync(order, "api-delete", Environment.UserName, cancellationToken);
		context.ExecutiveOrders.Remove(order);
		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.DeleteExecutiveOrderAsync(order, cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"onrush.delete-order",
			subjectType: nameof(ExecutiveOrder),
			subjectId: order.Id,
			subjectTitle: order.Title,
			temporalKind: "database",
			temporalEntryKey: graveyardEntry.EntryKey,
			temporalEntityType: graveyardEntry.EntityType,
			temporalEntityId: graveyardEntry.EntityId,
			temporalEntityTitle: graveyardEntry.EntityTitle,
			cancellationToken: cancellationToken);
	}

	/// <summary>
	/// Resolves the manual PUCK input for an executive order from its owning onrush identifier.
	/// The numeric part is trimmed of padding per PEP099 (<c>x0180</c> composes orders as <c>x180-o01</c>).
	/// </summary>
	private string ResolveOnrushNumericPart(string onrushSprintId)
	{
		var tokenization = puckTokenizer.TokenizeFor<OnrushSprint>(onrushSprintId);
		var numericValue = tokenization.Segments[0].NumericValue
			?? throw new InvalidOperationException($"Onrush sprint '{onrushSprintId}' does not carry a numeric PUCK part for executive order composition.");
		return numericValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
	}

	private static void ValidateEffectiveWindow(DateOnly? effectiveFrom, DateOnly? effectiveUntil)
	{
		if (effectiveFrom is not null && effectiveUntil is not null && effectiveFrom > effectiveUntil)
		{
			throw new ArgumentException("Executive order effective-from date cannot be later than its effective-until date.");
		}
	}

	private static OnrushSprint Clone(OnrushSprint sprint)
	{
		return new OnrushSprint
		{
			Id = sprint.Id,
			Title = sprint.Title,
			StartDate = sprint.StartDate,
			EndDate = sprint.EndDate,
		};
	}

	private static ExecutiveOrder Clone(ExecutiveOrder order)
	{
		return new ExecutiveOrder
		{
			Id = order.Id,
			Title = order.Title,
			OnrushSprintId = order.OnrushSprintId,
			Summary = order.Summary,
			EffectiveFrom = order.EffectiveFrom,
			EffectiveUntil = order.EffectiveUntil,
		};
	}
}