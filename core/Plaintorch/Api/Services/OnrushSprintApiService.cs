using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Enforcement;
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
	VaultWriteQueue writeQueue,
	VaultTemporalDataService temporalDataService,
	VaultAuditLogService auditLogService,
	StateDecisionService decisions) : IOnrushSprintApi
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
		await writeQueue.WriteAsync(sprint, cancellationToken: cancellationToken);
		await AttachMilestoneAsync(sprint, cancellationToken);
		await auditLogService.WriteAsync("api", "onrush.plan", subject: sprint, cancellationToken: cancellationToken);
		return sprint;
	}

	/// <summary>
	/// Gives a sprint its milestone checkpoint (PEP102), unless it already has one, and saves it. The checkpoint is
	/// tracked by the sprint like an objective, and named as its milestone; it carries no toll or external condition,
	/// so it stands purely for the sprint's completion.
	/// </summary>
	/// <remarks>
	/// The sprint must already be saved: the sprint and its milestone name each other, and one save cannot insert both
	/// sides of that cycle. Both links are database-only, so this save writes no note. (The old order leaned on minting the
	/// milestone's id, which saved the sprint on the caller's context as a side effect.)
	/// </remarks>
	private async Task AttachMilestoneAsync(OnrushSprint sprint, CancellationToken cancellationToken)
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
		await context.SaveChangesAsync(cancellationToken);
	}

	/// <inheritdoc />
	public async Task<OnrushSprint> BeginAsync(string onrushSprintId, DateOnly? startDate = null, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(onrushSprintId);
		var sprint = await context.OnrushSprints
			.Include(item => item.Objectives)
			.Include(item => item.Directives)
			.Include(item => item.MilestoneCheckpoint)
			.FirstOrDefaultAsync(item => item.Id == onrushSprintId, cancellationToken)
			?? throw new InvalidOperationException($"Onrush sprint '{onrushSprintId}' was not found.");

		switch (CyclePhases.Of(sprint))
		{
			case CyclePhase.Active:
				return sprint;
			case CyclePhase.Ended:
				// Nothing a save could read says this is a begin when no new start is named, so the verb asks itself.
				var subject = StateJudge.SubjectOf(sprint);
				decisions.RefuseOutright(StateJudge.SprintRestartRule, subject, StateJudge.RestartMessage(subject)).ThrowIfStopped();
				break;
		}

		var previous = Clone(sprint);
		// A plan's date is when it was meant to begin; beginning it now on a day still to come would be refused (PEP103
		// patch 5, onrush.early-start), so a plan dated ahead begins today. A date named by the caller is taken as named.
		var today = DateOnly.FromDateTime(DateTime.Today);
		var resolvedStartDate = startDate ?? (sprint.StartDate is { } planned && planned <= today ? planned : today);

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

			foreach (var directive in sprint.Directives)
			{
				directive.OnrushSprintId = activatedSprint.Id;
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

			context.OnrushSprints.Remove(sprint);
			await writeQueue.WriteAsync(activatedSprint, previous, cancellationToken);
			// Only a placeholder that carried no milestone leaves the real sprint without one; it gets its own now.
			await AttachMilestoneAsync(activatedSprint, cancellationToken);
			await auditLogService.WriteAsync("api", "onrush.begin", subject: activatedSprint, cancellationToken: cancellationToken);
			return activatedSprint;
		}

		sprint.StartDate = resolvedStartDate;
		await writeQueue.WriteAsync(sprint, previous, cancellationToken);
		await AttachMilestoneAsync(sprint, cancellationToken);
		await auditLogService.WriteAsync("api", "onrush.begin", subject: sprint, cancellationToken: cancellationToken);
		return sprint;
	}

	/// <inheritdoc />
	public async Task<OnrushSprint> StartNewAsync(DateOnly? startDate = null, CancellationToken cancellationToken = default)
	{
		// "Start new" ensures a sprint is active, as Polaris's does: the running one is kept, a plan is begun rather than
		// left behind, and a sprint is created only when there is neither.
		if (await stateService.GetActiveOnrushSprintAsync(cancellationToken) is { } active)
		{
			return active;
		}

		if (await context.OnrushSprints.AnyAsync(sprint => sprint.Id == OnrushSprint.PlanningPlaceholderId, cancellationToken))
		{
			return await BeginAsync(OnrushSprint.PlanningPlaceholderId, startDate, cancellationToken);
		}

		var resolvedStartDate = startDate ?? DateOnly.FromDateTime(DateTime.Today);
		var sprint = new OnrushSprint
		{
			Id = puckCreationService.CreateIdFor<OnrushSprint>(systemSegments: [new PuckSegmentInput(Date: resolvedStartDate)]),
			Title = PlaintorchDefaultTitleFactory.CreateOnrushTitle(resolvedStartDate),
			StartDate = resolvedStartDate,
		};

		context.OnrushSprints.Add(sprint);
		await writeQueue.WriteAsync(sprint, cancellationToken: cancellationToken);
		await AttachMilestoneAsync(sprint, cancellationToken);
		await auditLogService.WriteAsync("api", "onrush.start-new", subject: sprint, cancellationToken: cancellationToken);
		return sprint;
	}

	/// <inheritdoc />
	public async Task<OnrushSprint> EnsureAsync(OnrushSlot slot, CancellationToken cancellationToken = default)
	{
		if (slot == OnrushSlot.Active)
		{
			return await StartNewAsync(cancellationToken: cancellationToken);
		}

		return await context.OnrushSprints.FirstOrDefaultAsync(sprint => sprint.Id == OnrushSprint.PlanningPlaceholderId, cancellationToken)
			?? await PlanAsync(new OnrushSprintPlan(PlaintorchDefaultTitleFactory.CreateOnrushPlanTitle()), cancellationToken);
	}

	/// <inheritdoc />
	public async Task<OnrushSprint> EndAsync(string onrushSprintId, DateOnly? endDate = null, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(onrushSprintId);
		var sprint = await context.OnrushSprints.FirstOrDefaultAsync(item => item.Id == onrushSprintId, cancellationToken)
			?? throw new InvalidOperationException($"Onrush sprint '{onrushSprintId}' was not found.");

		// No sprint ends twice or before it begins (PEP103 patch 4). The save would be refused too, but ending again on the
		// day it ended changes nothing a save could read.
		var subject = StateJudge.SubjectOf(sprint);
		switch (CyclePhases.Of(sprint))
		{
			case CyclePhase.Ended:
				decisions.RefuseOutright(StateJudge.SprintRestartRule, subject, StateJudge.EndedMessage(subject)).ThrowIfStopped();
				break;
			case CyclePhase.Planned:
				decisions.RefuseOutright(StateJudge.SprintEndRule, subject, StateJudge.UnbegunEndMessage(subject)).ThrowIfStopped();
				break;
		}

		var previous = Clone(sprint);
		sprint.EndDate = endDate ?? DateOnly.FromDateTime(DateTime.Today);
		await writeQueue.WriteAsync(sprint, previous, cancellationToken);
		await auditLogService.WriteAsync("api", "onrush.end", subject: sprint, cancellationToken: cancellationToken);
		return sprint;
	}

	/// <inheritdoc />
	public async Task DeleteAsync(string onrushSprintId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(onrushSprintId);
		var sprint = await context.OnrushSprints
			.Include(item => item.Objectives)
			.Include(item => item.Directives)
			.Include(item => item.ExecutiveOrders)
			.Include(item => item.MilestoneCheckpoint)
			.FirstOrDefaultAsync(item => item.Id == onrushSprintId, cancellationToken)
			?? throw new InvalidOperationException($"Onrush sprint '{onrushSprintId}' was not found.");

		// The milestone is the sprint's own — the one deletion the checkpoint service refuses on its own terms
		// is exactly this one, done because the sprint is going. The sprint and its milestone point at each
		// other, so the cycle is broken in this first save, and both go in the second; deleting both ends of the
		// cycle in one save leaves EF unable to order the commands. Breaking it only lets the milestone leave the
		// sprint, which an ended sprint allows (PEP103 patch 5): what it holds stays as it was until the sprint goes.
		var milestone = sprint.MilestoneCheckpoint;
		if (milestone is not null)
		{
			sprint.MilestoneCheckpointId = null;
			sprint.MilestoneCheckpoint = null;
			milestone.OnrushSprintId = null;
			milestone.OnrushSprint = null;
			await context.SaveChangesAsync(cancellationToken);
		}

		// Executive orders belong to the sprint outright, so they go with it — markdown and all. Their notes are archived
		// first, while nothing is pending removal: archiving saves, and what an ended sprint holds may only go in the save
		// that removes the sprint (PEP103 patch 5).
		var orders = sprint.ExecutiveOrders.ToList();
		foreach (var order in orders)
		{
			await markdownFileService.DeleteExecutiveOrderAsync(order, cancellationToken);
		}

		foreach (var order in orders)
		{
			temporalDataService.StageEntityArchive(order, "api-delete", Environment.UserName);
			context.ExecutiveOrders.Remove(order);
		}

		if (milestone is not null)
		{
			context.Checkpoints.Remove(milestone);
		}

		// The sprint no longer names a milestone, so it can go. Its SetNull foreign keys free the objectives, the
		// directives and any other checkpoints it merely tracked — they live on, detached — and EF drops the reference
		// on each tracked one, which is what the markdown re-save below then records.
		var detached = sprint.Objectives.Cast<object>().Concat(sprint.Directives).ToList();
		var graveyardEntry = temporalDataService.StageEntityArchive(sprint, "api-delete", Environment.UserName);
		context.OnrushSprints.Remove(sprint);
		await context.SaveChangesAsync(cancellationToken);

		foreach (var member in detached)
		{
			await writeQueue.WriteAsync(member, cancellationToken: cancellationToken);
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
			.Include(sprint => sprint.Directives)
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
			.Include(sprint => sprint.Directives)
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
			.Include(sprint => sprint.Directives)
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

		await writeQueue.WriteAsync(sprint, previous, cancellationToken);
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
	public async Task<IReadOnlyList<Objective>> AssignAllOnboardObjectivesToSelfAsync(string onrushSprintId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(onrushSprintId);
		var sprintExists = await context.OnrushSprints.AnyAsync(sprint => sprint.Id == onrushSprintId, cancellationToken);
		if (!sprintExists)
		{
			throw new InvalidOperationException($"Onrush sprint '{onrushSprintId}' was not found.");
		}

		var objectives = await context.Objectives
			.Where(objective => objective.Status == ObjectiveStatus.Onboard)
			.ToListAsync(cancellationToken);

		foreach (var objective in objectives)
		{
			objective.OnrushSprintId = onrushSprintId;
		}

		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"onrush.assign-onboard-objectives",
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
		await writeQueue.WriteAsync(order, cancellationToken: cancellationToken);
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
		await writeQueue.WriteAsync(order, previous, cancellationToken);
		await auditLogService.WriteAsync("api", "onrush.update-order", subject: order, cancellationToken: cancellationToken);
		return order;
	}

	/// <inheritdoc />
	public async Task DeleteExecutiveOrderAsync(string executiveOrderId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(executiveOrderId);
		var order = await context.ExecutiveOrders.FirstOrDefaultAsync(item => item.Id == executiveOrderId, cancellationToken)
			?? throw new InvalidOperationException($"Executive order '{executiveOrderId}' was not found.");

		var graveyardEntry = temporalDataService.StageEntityArchive(order, "api-delete", Environment.UserName);
		context.ExecutiveOrders.Remove(order);
		await writeQueue.RecordRemoveAsync(order, cancellationToken);
		await context.SaveChangesAsync(cancellationToken);
		await writeQueue.DrainRemoveAsync(order, cancellationToken);
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