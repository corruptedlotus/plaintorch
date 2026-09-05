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
			Id = OnrushSprint.PlanningPlaceholderId,
			Title = plan.Title,
			StartDate = plan.StartDate,
			EndDate = plan.EndDate,
		};

		context.OnrushSprints.Add(sprint);
		AttachMilestone(sprint);
		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SaveOnrushSprintAsync(sprint, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "onrush.plan", subject: sprint, cancellationToken: cancellationToken);
		return sprint;
	}

	/// <summary>
	/// Gives a sprint its milestone checkpoint (PEP102), unless it already has one. The checkpoint is tracked
	/// by the sprint like an objective, and named as its milestone; it carries no toll or external condition,
	/// so it stands purely for the sprint's completion.
	/// </summary>
	private void AttachMilestone(OnrushSprint sprint)
	{
		if (!string.IsNullOrWhiteSpace(sprint.MilestoneCheckpointId))
		{
			return;
		}

		var milestone = new Checkpoint
		{
			Id = puckCreationService.CreateIdFor<Checkpoint>(),
			Title = $"{sprint.Title} milestone",
			OnrushSprintId = sprint.Id,
		};

		context.Checkpoints.Add(milestone);
		sprint.MilestoneCheckpointId = milestone.Id;
		sprint.MilestoneCheckpoint = milestone;
	}

	/// <inheritdoc />
	public async Task<OnrushSprint> BeginAsync(string onrushSprintId, DateOnly? startDate = null, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(onrushSprintId);
		var sprint = await context.OnrushSprints
			.Include(item => item.Objectives)
			.Include(item => item.MilestoneCheckpoint)
			.FirstOrDefaultAsync(item => item.Id == onrushSprintId, cancellationToken)
			?? throw new InvalidOperationException($"Onrush sprint '{onrushSprintId}' was not found.");

		if (sprint.StartDate is not null && sprint.EndDate is null && sprint.Id != OnrushSprint.PlanningPlaceholderId)
		{
			return sprint;
		}

		var previous = Clone(sprint);
		var resolvedStartDate = startDate ?? sprint.StartDate ?? DateOnly.FromDateTime(DateTime.Today);

		if (sprint.Id == OnrushSprint.PlanningPlaceholderId)
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

			// Carry the milestone (and any other tracked checkpoints) onto the real sprint before the
			// placeholder is removed, so the milestone begun with the sprint is the one it planned with rather
			// than a fresh one. A placeholder from before this feature has none, so one is attached instead.
			var trackedCheckpoints = await context.Checkpoints
				.Where(checkpoint => checkpoint.OnrushSprintId == sprint.Id)
				.ToListAsync(cancellationToken);
			foreach (var checkpoint in trackedCheckpoints)
			{
				checkpoint.OnrushSprintId = activatedSprint.Id;
			}

			if (!string.IsNullOrWhiteSpace(sprint.MilestoneCheckpointId))
			{
				activatedSprint.MilestoneCheckpointId = sprint.MilestoneCheckpointId;
				// Detach the milestone from the placeholder first: its row is about to be removed, and the FK
				// would otherwise still name it.
				sprint.MilestoneCheckpointId = null;
			}
			else
			{
				AttachMilestone(activatedSprint);
			}

			context.OnrushSprints.Remove(sprint);
			await context.SaveChangesAsync(cancellationToken);
			await markdownFileService.SaveOnrushSprintAsync(activatedSprint, previous, cancellationToken: cancellationToken);
			await auditLogService.WriteAsync("api", "onrush.begin", subject: activatedSprint, cancellationToken: cancellationToken);
			return activatedSprint;
		}

		sprint.StartDate = resolvedStartDate;
		AttachMilestone(sprint);
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
		AttachMilestone(sprint);
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
	public async Task DeleteAsync(string onrushSprintId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(onrushSprintId);
		var sprint = await context.OnrushSprints
			.Include(item => item.Objectives)
			.Include(item => item.ExecutiveOrders)
			.Include(item => item.MilestoneCheckpoint)
			.FirstOrDefaultAsync(item => item.Id == onrushSprintId, cancellationToken)
			?? throw new InvalidOperationException($"Onrush sprint '{onrushSprintId}' was not found.");

		// Executive orders belong to the sprint outright, so they go with it — markdown and all.
		foreach (var order in sprint.ExecutiveOrders.ToList())
		{
			await temporalDataService.ArchiveEntityAsync(order, "api-delete", Environment.UserName, cancellationToken);
			context.ExecutiveOrders.Remove(order);
			await markdownFileService.DeleteExecutiveOrderAsync(order, cancellationToken);
		}

		// The milestone is the sprint's own — the one deletion the checkpoint service refuses on its own terms
		// is exactly this one, done because the sprint is going. The sprint and its milestone point at each
		// other, so the cycle is broken and the milestone removed in this first save, before the sprint itself
		// is deleted below; deleting both ends of the cycle in one save leaves EF unable to order the commands.
		if (sprint.MilestoneCheckpoint is { } milestone)
		{
			sprint.MilestoneCheckpointId = null;
			sprint.MilestoneCheckpoint = null;
			milestone.OnrushSprintId = null;
			milestone.OnrushSprint = null;
			context.Checkpoints.Remove(milestone);
		}

		await context.SaveChangesAsync(cancellationToken);

		// The sprint no longer names a milestone, so it can go. Its SetNull foreign keys free the objectives and
		// any other checkpoints it merely tracked — they live on, detached — and EF drops the reference on each
		// tracked one, which is what the markdown re-save below then records.
		var detached = sprint.Objectives.ToList();
		var graveyardEntry = await temporalDataService.ArchiveEntityAsync(sprint, "api-delete", Environment.UserName, cancellationToken);
		context.OnrushSprints.Remove(sprint);
		await context.SaveChangesAsync(cancellationToken);

		foreach (var objective in detached)
		{
			await markdownFileService.SaveObjectiveAsync(objective, cancellationToken: cancellationToken);
		}

		await markdownFileService.DeleteOnrushSprintAsync(sprint, cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"onrush.delete",
			subjectType: nameof(OnrushSprint),
			subjectId: sprint.Id,
			subjectTitle: sprint.Title,
			temporalKind: "database",
			temporalEntryKey: graveyardEntry.EntryKey,
			temporalEntityType: graveyardEntry.EntityType,
			temporalEntityId: graveyardEntry.EntityId,
			temporalEntityTitle: graveyardEntry.EntityTitle,
			cancellationToken: cancellationToken);
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
				.Include(sprint => sprint.Checkpoints)
				.Include(sprint => sprint.MilestoneCheckpoint)
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
			.Include(sprint => sprint.Checkpoints)
			.Include(sprint => sprint.MilestoneCheckpoint)
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
			.Include(sprint => sprint.Checkpoints)
			.Include(sprint => sprint.MilestoneCheckpoint)
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

		if (update.StartDate.IsSet)
		{
			sprint.StartDate = update.StartDate.Value;
		}

		if (update.EndDate.IsSet)
		{
			sprint.EndDate = update.EndDate.Value;
		}

		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SaveOnrushSprintAsync(sprint, previous, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "onrush.update", subject: sprint, cancellationToken: cancellationToken);
		return sprint;
	}

	/// <inheritdoc />
	public async Task SetGraphLayoutAsync(string onrushSprintId, string? graphLayout, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(onrushSprintId);
		var sprint = await context.OnrushSprints.FirstOrDefaultAsync(item => item.Id == onrushSprintId, cancellationToken)
			?? throw new InvalidOperationException($"Onrush sprint '{onrushSprintId}' was not found.");

		// The column only; no markdown rewrite and no audit entry. Layout is UI state, written on every drag,
		// and rewriting the sprint's note or logging each nudge would be churn for something the vault never sees.
		sprint.GraphLayout = graphLayout;
		await context.SaveChangesAsync(cancellationToken);
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
		if (sprint.Id == OnrushSprint.PlanningPlaceholderId)
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
	public async Task<ExecutiveOrder?> GetExecutiveOrderAsync(string executiveOrderId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(executiveOrderId);
		return await context.ExecutiveOrders
			.AsNoTracking()
			.FirstOrDefaultAsync(order => order.Id == executiveOrderId, cancellationToken);
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

		if (update.Summary.IsSet)
		{
			order.Summary = string.IsNullOrWhiteSpace(update.Summary.Value) ? null : update.Summary.Value;
		}

		if (update.EffectiveFrom.IsSet)
		{
			order.EffectiveFrom = update.EffectiveFrom.Value;
		}

		if (update.EffectiveUntil.IsSet)
		{
			order.EffectiveUntil = update.EffectiveUntil.Value;
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