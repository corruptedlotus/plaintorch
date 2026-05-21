using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Puck;
using Pleiades.Saga;
using Pleiades.Vault.Database;
using Pleiades.Vault.Markdown;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Executes watcher discovery decisions by reconciling markdown candidates with database state and canonical markdown metadata.
/// Watcher rewrites may normalize frontmatter, file name, and path, but must preserve markdown body content.
/// </summary>
public sealed class VaultWatcherSyncService(
	PlainfraContext context,
	PuckCreationService puckCreationService,
	PlaintorchMarkdownStorageService markdownStorageService,
	VaultTemporalDataService temporalDataService,
	VaultAuditLogService auditLogService,
	VaultWatcherWriteBarrier writeBarrier,
	ILogger<VaultWatcherSyncService> logger)
{
	/// <summary>
	/// Executes the suggested reconciliation action for a discovered candidate.
	/// </summary>
	public async Task ExecuteAsync(VaultSyncCandidate candidate, string origin, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(candidate);
		ArgumentException.ThrowIfNullOrWhiteSpace(origin);

		switch (candidate.SuggestedAction)
		{
			case VaultSyncAction.CreateFromFile:
				await CreateFromFileAsync(candidate, origin, cancellationToken);
				break;
			case VaultSyncAction.UpdateFromFile:
				await UpdateFromFileAsync(candidate, origin, cancellationToken);
				break;
			case VaultSyncAction.RewriteFromDatabase:
				await RewriteFromDatabaseAsync(candidate, origin, cancellationToken);
				break;
			case VaultSyncAction.PurgeFile:
				await PurgeFileAsync(candidate, origin, cancellationToken);
				break;
			case VaultSyncAction.Ignore:
				await auditLogService.WriteAsync(
					"sync",
					"watcher-ignore",
					subjectType: candidate.Model.EntityName,
					subjectId: candidate.PathId,
					subjectTitle: candidate.PathTitle,
					details: new { origin, candidate.VaultRelativePath, candidate.SuggestedReason },
					cancellationToken: cancellationToken);
				break;
			case VaultSyncAction.Conflict:
				await auditLogService.WriteAsync(
					"sync",
					"watcher-conflict",
					subjectType: candidate.Model.EntityName,
					subjectId: candidate.PathId,
					subjectTitle: candidate.PathTitle,
					details: new
					{
						origin,
						candidate.VaultRelativePath,
						candidate.SuggestedReason,
						issues = candidate.Issues.Select(issue => new { issue.FieldPath, issue.Message, issue.RawValue }).ToArray(),
					},
					cancellationToken: cancellationToken);
				break;
		}
	}

	private async Task CreateFromFileAsync(VaultSyncCandidate candidate, string origin, CancellationToken cancellationToken)
	{
		var model = candidate.ParsedModel;
		if (model is not IPuckNamedEntity namedEntity)
		{
			throw new InvalidOperationException($"Watcher create-from-file requires a PUCK-named model, but '{candidate.Model.EntityName}' is not PUCK-backed.");
		}

		var ignoredPathId = default(string);
		if (!puckCreationService.RequiresCallerInputFor(candidate.Model.EntityType))
		{
			ignoredPathId = string.IsNullOrWhiteSpace(candidate.PathId) ? null : candidate.PathId;
			namedEntity.Id = puckCreationService.CreateIdFor(candidate.Model.EntityType);
		}
		else if (string.IsNullOrWhiteSpace(namedEntity.Id))
		{
			namedEntity.Id = puckCreationService.CreateIdFor(candidate.Model.EntityType);
		}

		context.Add(model);
		await context.SaveChangesAsync(cancellationToken);
		await SaveCanonicalMarkdownAsync(model, sourcePath: candidate.AbsolutePath, cancellationToken: cancellationToken);

		if (ignoredPathId is null)
		{
			logger.LogInformation(
				"Watcher created {EntityType} '{EntityId}' from '{Path}' and rewrote canonical markdown.",
				candidate.Model.EntityName,
				namedEntity.Id,
				candidate.VaultRelativePath);
		}
		else
		{
			logger.LogInformation(
				"Watcher created {EntityType} '{EntityId}' from '{Path}' and rewrote canonical markdown, ignoring supplied path id '{IgnoredPathId}'.",
				candidate.Model.EntityName,
				namedEntity.Id,
				candidate.VaultRelativePath,
				ignoredPathId);
		}

		await auditLogService.WriteAsync(
			"sync",
			"create-from-file",
			subject: model,
			details: new { origin, candidate.VaultRelativePath, candidate.SuggestedReason, ignoredPathId },
			cancellationToken: cancellationToken);
	}

	private async Task UpdateFromFileAsync(VaultSyncCandidate candidate, string origin, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(candidate.PathId))
		{
			await CreateFromFileAsync(candidate, origin, cancellationToken);
			return;
		}

		var existing = await LoadExistingAsync(candidate.Model.EntityType, candidate.PathId, cancellationToken);
		if (existing is null)
		{
			await CreateFromFileAsync(candidate, origin, cancellationToken);
			return;
		}

		var previous = CloneEntity(existing);
		context.Entry(existing).CurrentValues.SetValues(candidate.ParsedModel);
		if (existing is IPuckNamedEntity namedEntity && !string.IsNullOrWhiteSpace(candidate.PathTitle))
		{
			namedEntity.Title = candidate.PathTitle;
		}
		await context.SaveChangesAsync(cancellationToken);
		await SaveCanonicalMarkdownAsync(existing, previous, candidate.AbsolutePath, cancellationToken);

		logger.LogInformation(
			"Watcher updated {EntityType} '{EntityId}' from '{Path}' and rewrote canonical markdown.",
			candidate.Model.EntityName,
			candidate.PathId,
			candidate.VaultRelativePath);

		await auditLogService.WriteAsync(
			"sync",
			"update-from-file",
			subject: existing,
			details: new { origin, candidate.VaultRelativePath, candidate.SuggestedReason },
			cancellationToken: cancellationToken);
	}

	private async Task RewriteFromDatabaseAsync(VaultSyncCandidate candidate, string origin, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(candidate.PathId))
		{
			await auditLogService.WriteAsync(
				"sync",
				"rewrite-skipped",
				subjectType: candidate.Model.EntityName,
				subjectTitle: candidate.PathTitle,
				details: new { origin, candidate.VaultRelativePath, reason = "No database identifier was available for canonical rewrite." },
				cancellationToken: cancellationToken);
			return;
		}

		var existing = await LoadExistingAsync(candidate.Model.EntityType, candidate.PathId, cancellationToken);
		if (existing is null)
		{
			await auditLogService.WriteAsync(
				"sync",
				"rewrite-skipped",
				subjectType: candidate.Model.EntityName,
				subjectId: candidate.PathId,
				subjectTitle: candidate.PathTitle,
				details: new { origin, candidate.VaultRelativePath, reason = "Database entity no longer exists for canonical rewrite." },
				cancellationToken: cancellationToken);
			return;
		}

		await SaveCanonicalMarkdownAsync(existing, sourcePath: candidate.AbsolutePath, cancellationToken: cancellationToken);

		logger.LogInformation(
			"Watcher rewrote canonical {EntityType} '{EntityId}' markdown over '{Path}'.",
			candidate.Model.EntityName,
			candidate.PathId,
			candidate.VaultRelativePath);

		await auditLogService.WriteAsync(
			"sync",
			"rewrite-from-database",
			subject: existing,
			details: new { origin, candidate.VaultRelativePath, candidate.SuggestedReason },
			cancellationToken: cancellationToken);
	}

	private async Task PurgeFileAsync(VaultSyncCandidate candidate, string origin, CancellationToken cancellationToken)
	{
		writeBarrier.Suppress(candidate.AbsolutePath);
		var archived = await temporalDataService.ArchivePathAsync(
			candidate.AbsolutePath,
			"watcher-purge",
			candidate.Model.EntityName,
			candidate.PathId,
			candidate.PathTitle,
			Environment.UserName,
			cancellationToken);

		logger.LogInformation(
			"Watcher purged '{Path}' for {EntityType}.",
			candidate.VaultRelativePath,
			candidate.Model.EntityName);

		await auditLogService.WriteAsync(
			"sync",
			"purge-file",
			subjectType: candidate.Model.EntityName,
			subjectId: candidate.PathId,
			subjectTitle: candidate.PathTitle,
			temporalKind: archived is null ? null : "file",
			temporalEntryKey: archived?.EntryKey,
			temporalEntityType: archived?.EntityType,
			temporalEntityId: archived?.EntityId,
			temporalEntityTitle: archived?.EntityTitle,
			temporalLocation: archived?.ArchivedRelativePath,
			details: new { origin, candidate.VaultRelativePath, candidate.SuggestedReason },
			cancellationToken: cancellationToken);
	}

	private async Task<object?> LoadExistingAsync(Type entityType, string id, CancellationToken cancellationToken)
	{
		if (entityType == typeof(Directive))
		{
			return await context.Directives.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
		}

		if (entityType == typeof(Objective))
		{
			return await context.Objectives.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
		}

		if (entityType == typeof(OnrushSprint))
		{
			return await context.OnrushSprints.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
		}

		if (entityType == typeof(PolarisCycle))
		{
			return await context.PolarisCycles.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
		}

		if (entityType == typeof(LorePage))
		{
			return await context.LorePages.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
		}

		throw new InvalidOperationException($"Watcher synchronization does not support entity type '{entityType.Name}'.");
	}

	private async Task SaveCanonicalMarkdownAsync(object entity, object? previous = null, string? sourcePath = null, CancellationToken cancellationToken = default)
	{
		switch (entity)
		{
			case Directive directive:
				await markdownStorageService.SaveDirectiveAsync(directive, previous as Directive, sourcePath, cancellationToken);
				break;
			case Objective objective:
				await markdownStorageService.SaveObjectiveAsync(objective, previous as Objective, sourcePath, cancellationToken);
				break;
			case OnrushSprint sprint:
				await markdownStorageService.SaveOnrushSprintAsync(sprint, previous as OnrushSprint, sourcePath, cancellationToken);
				break;
			case PolarisCycle cycle:
				await markdownStorageService.SavePolarisCycleAsync(cycle, previous as PolarisCycle, sourcePath: sourcePath, cancellationToken: cancellationToken);
				break;
			case LorePage lorePage:
				await markdownStorageService.SaveLorePageAsync(lorePage, previous as LorePage, sourcePath: sourcePath, cancellationToken: cancellationToken);
				break;
			default:
				throw new InvalidOperationException($"Watcher synchronization does not support markdown save for entity type '{entity.GetType().Name}'.");
		}
	}

	private static object CloneEntity(object entity)
	{
		return entity switch
		{
			Directive directive => new Directive
			{
				Id = directive.Id,
				Title = directive.Title,
				Codename = directive.Codename,
				ParentDirectiveId = directive.ParentDirectiveId,
				Status = directive.Status,
				Tags = directive.Tags.ToList(),
				Due = directive.Due,
				AlternativeLoreDirectory = directive.AlternativeLoreDirectory,
				StartDate = directive.StartDate,
				EndDate = directive.EndDate,
			},
			Objective objective => new Objective
			{
				Id = objective.Id,
				Title = objective.Title,
				DirectiveId = objective.DirectiveId,
				OnrushSprintId = objective.OnrushSprintId,
				College = objective.College,
				Status = objective.Status,
				CelestronValue = objective.CelestronValue,
				IsEnduring = objective.IsEnduring,
			},
			OnrushSprint sprint => new OnrushSprint
			{
				Id = sprint.Id,
				Title = sprint.Title,
				StartDate = sprint.StartDate,
				EndDate = sprint.EndDate,
			},
			PolarisCycle cycle => new PolarisCycle
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
			},
			LorePage lorePage => new LorePage
			{
				Id = lorePage.Id,
				Title = lorePage.Title,
				ParentId = lorePage.ParentId,
				Level = lorePage.Level,
				RelativePath = lorePage.RelativePath,
				Era = lorePage.Era,
				Chapter = lorePage.Chapter,
				Act = lorePage.Act,
				Phase = lorePage.Phase,
				IndexedUtc = lorePage.IndexedUtc,
			},
			_ => throw new InvalidOperationException($"Watcher synchronization cannot clone entity type '{entity.GetType().Name}'."),
		};
	}
}
