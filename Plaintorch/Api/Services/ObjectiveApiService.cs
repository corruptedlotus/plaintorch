using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Api.Services;

/// <summary>
/// Implements the objective-facing PLAINTORCH application API.
/// </summary>
public sealed class ObjectiveApiService(
	PlainfraContext context,
	PuckCreationService puckCreationService,
	PlaintorchMarkdownStorageService markdownStorageService,
	VaultTemporalDataService temporalDataService,
	VaultAuditLogService auditLogService) : IObjectiveApi
{
	/// <inheritdoc />
	public Task<Objective?> GetAsync(string objectiveId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(objectiveId);
		return context.Objectives
			.AsNoTracking()
			.FirstOrDefaultAsync(objective => objective.Id == objectiveId, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Objective>> ListAsync(CancellationToken cancellationToken = default)
	{
		return await context.Objectives
			.AsNoTracking()
			.OrderBy(objective => objective.Title)
			.ToListAsync(cancellationToken);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Objective>> FindAsync(SearchRequest search, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(search);
		ArgumentException.ThrowIfNullOrWhiteSpace(search.Query);

		var query = context.Objectives
			.AsNoTracking()
			.Where(objective => objective.Id.Contains(search.Query) || objective.Title.Contains(search.Query));

		if (search.Take is > 0)
		{
			query = query.Take(search.Take.Value);
		}

		return await query
			.OrderBy(objective => objective.Title)
			.ToListAsync(cancellationToken);
	}

	/// <inheritdoc />
	public Task<Objective> CreateStandaloneAsync(string title, bool isEnduring = false, string? requestedId = null, CancellationToken cancellationToken = default)
	{
		return CreateInternalAsync(title, null, null, isEnduring, requestedId, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<Objective> CreateFromDirectiveAsync(string directiveId, string title, string? onrushSprintId = null, bool isEnduring = false, string? requestedId = null, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directiveId);
		var directiveExists = await context.Directives.AnyAsync(directive => directive.Id == directiveId, cancellationToken);
		if (!directiveExists)
		{
			throw new InvalidOperationException($"Directive '{directiveId}' was not found.");
		}

		if (!string.IsNullOrWhiteSpace(onrushSprintId))
		{
			var sprintExists = await context.OnrushSprints.AnyAsync(sprint => sprint.Id == onrushSprintId, cancellationToken);
			if (!sprintExists)
			{
				throw new InvalidOperationException($"Onrush sprint '{onrushSprintId}' was not found.");
			}
		}

		return await CreateInternalAsync(title, directiveId, onrushSprintId, isEnduring, requestedId, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<Objective> UpdateAsync(string objectiveId, ObjectiveUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(objectiveId);
		ArgumentNullException.ThrowIfNull(update);

		var objective = await context.Objectives.FirstOrDefaultAsync(item => item.Id == objectiveId, cancellationToken)
			?? throw new InvalidOperationException($"Objective '{objectiveId}' was not found.");
		var previous = Clone(objective);

		if (!string.IsNullOrWhiteSpace(update.Title))
		{
			objective.Title = update.Title;
		}

		if (!string.IsNullOrWhiteSpace(update.DirectiveId))
		{
			objective.DirectiveId = update.DirectiveId;
		}

		if (!string.IsNullOrWhiteSpace(update.OnrushSprintId))
		{
			objective.OnrushSprintId = update.OnrushSprintId;
		}

		if (update.College is not null)
		{
			objective.College = update.College.Value;
		}

		if (update.CelestronValue is not null)
		{
			objective.CelestronValue = update.CelestronValue.Value;
		}

		if (update.IsEnduring is not null)
		{
			objective.IsEnduring = update.IsEnduring.Value;
		}

		await context.SaveChangesAsync(cancellationToken);
		await markdownStorageService.SaveObjectiveAsync(objective, previous, cancellationToken);
		await auditLogService.WriteAsync("api", "objective.update", subject: objective, cancellationToken: cancellationToken);
		return objective;
	}

	/// <inheritdoc />
	public async Task<Objective> ShiftWorkflowAsync(string objectiveId, ObjectiveWorkflowShift shift, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(objectiveId);
		ArgumentNullException.ThrowIfNull(shift);

		var objective = await context.Objectives.FirstOrDefaultAsync(item => item.Id == objectiveId, cancellationToken)
			?? throw new InvalidOperationException($"Objective '{objectiveId}' was not found.");

		objective.Status = shift.Status;
		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync("api", "objective.workflow-shift", subject: objective, cancellationToken: cancellationToken);
		return objective;
	}

	/// <inheritdoc />
	public async Task<Objective> AddToOnrushAsync(string objectiveId, string onrushSprintId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(objectiveId);
		ArgumentException.ThrowIfNullOrWhiteSpace(onrushSprintId);

		var objective = await context.Objectives.FirstOrDefaultAsync(item => item.Id == objectiveId, cancellationToken)
			?? throw new InvalidOperationException($"Objective '{objectiveId}' was not found.");

		var sprintExists = await context.OnrushSprints.AnyAsync(sprint => sprint.Id == onrushSprintId, cancellationToken);
		if (!sprintExists)
		{
			throw new InvalidOperationException($"Onrush sprint '{onrushSprintId}' was not found.");
		}

		objective.OnrushSprintId = onrushSprintId;
		objective.Status = ObjectiveStatus.Onrush;
		await context.SaveChangesAsync(cancellationToken);
		await markdownStorageService.SaveObjectiveAsync(objective, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "objective.add-to-onrush", subject: objective, cancellationToken: cancellationToken);
		return objective;
	}

	/// <inheritdoc />
	public async Task DeleteAsync(string objectiveId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(objectiveId);
		var objective = await context.Objectives
			.Include(item => item.Executives)
			.FirstOrDefaultAsync(item => item.Id == objectiveId, cancellationToken)
			?? throw new InvalidOperationException($"Objective '{objectiveId}' was not found.");

		if (objective.Executives.Count > 0)
		{
			throw new InvalidOperationException("Objective cannot be deleted while it still has executive records.");
		}

		var graveyardEntry = await temporalDataService.ArchiveEntityAsync(objective, "api-delete", Environment.UserName, cancellationToken);
		context.Objectives.Remove(objective);
		await context.SaveChangesAsync(cancellationToken);
		await markdownStorageService.DeleteObjectiveAsync(objective, cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"objective.delete",
			subjectType: nameof(Objective),
			subjectId: objective.Id,
			subjectTitle: objective.Title,
			temporalKind: "database",
			temporalEntryKey: graveyardEntry.EntryKey,
			temporalEntityType: graveyardEntry.EntityType,
			temporalEntityId: graveyardEntry.EntityId,
			temporalEntityTitle: graveyardEntry.EntityTitle,
			cancellationToken: cancellationToken);
	}

	private async Task<Objective> CreateInternalAsync(string title, string? directiveId, string? onrushSprintId, bool isEnduring, string? requestedId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(title);

		var objective = new Objective
		{
			Id = puckCreationService.CreateIdFor<Objective>(requestedId),
			Title = title,
			DirectiveId = directiveId,
			OnrushSprintId = onrushSprintId,
			IsEnduring = isEnduring,
		};

		context.Objectives.Add(objective);
		await context.SaveChangesAsync(cancellationToken);
		await markdownStorageService.SaveObjectiveAsync(objective, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "objective.create", subject: objective, cancellationToken: cancellationToken);
		return objective;
	}

	private static Objective Clone(Objective objective)
	{
		return new Objective
		{
			Id = objective.Id,
			Title = objective.Title,
			DirectiveId = objective.DirectiveId,
			OnrushSprintId = objective.OnrushSprintId,
			College = objective.College,
			Status = objective.Status,
			CelestronValue = objective.CelestronValue,
			IsEnduring = objective.IsEnduring,
		};
	}
}