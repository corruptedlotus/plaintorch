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
	PlaintorchStateService stateService,
	PlaintorchMarkdownStorageService markdownFileService,
	VaultAuditLogService auditLogService) : IOnrushSprintApi
{
	/// <inheritdoc />
	public async Task<OnrushSprint> PlanAsync(OnrushSprintPlan plan, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentException.ThrowIfNullOrWhiteSpace(plan.Title);

		var sprintDate = plan.StartDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
		var sprint = new OnrushSprint
		{
			Id = puckCreationService.CreateIdFor<OnrushSprint>(systemSegments: [new PuckSegmentInput(Date: sprintDate)]),
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
		var sprint = await context.OnrushSprints.FirstOrDefaultAsync(item => item.Id == onrushSprintId, cancellationToken)
			?? throw new InvalidOperationException($"Onrush sprint '{onrushSprintId}' was not found.");

		var previous = Clone(sprint);
		sprint.StartDate ??= startDate ?? DateOnly.FromDateTime(DateTime.Today);
		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SaveOnrushSprintAsync(sprint, previous, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "onrush.begin", subject: sprint, cancellationToken: cancellationToken);
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
				.FirstOrDefaultAsync(sprint => sprint.Id == onrushSprintId, cancellationToken);
		}

		return await stateService.GetActiveOnrushSprintAsync(cancellationToken);
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
}