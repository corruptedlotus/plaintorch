using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Api.Services;

/// <summary>
/// Implements the directive-facing PLAINTORCH application API.
/// </summary>
public sealed class DirectiveApiService(
	PlainfraContext context,
	PuckCreationService puckCreationService,
	PlaintorchMarkdownStorageService markdownFileService,
	VaultTemporalDataService temporalDataService,
	VaultAuditLogService auditLogService) : IDirectiveApi
{
	/// <inheritdoc />
	public Task<Directive?> GetAsync(string directiveId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directiveId);
		return context.Directives
			.AsNoTracking()
			.FirstOrDefaultAsync(directive => directive.Id == directiveId, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Directive>> ListAsync(CancellationToken cancellationToken = default)
	{
		return await context.Directives
			.AsNoTracking()
			.OrderBy(directive => directive.Title)
			.ToListAsync(cancellationToken);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Directive>> FindAsync(SearchRequest search, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(search);
		ArgumentException.ThrowIfNullOrWhiteSpace(search.Query);

		var query = context.Directives
			.AsNoTracking()
			.Where(directive =>
				directive.Id.Contains(search.Query)
				|| directive.Title.Contains(search.Query)
				|| (directive.Codename != null && directive.Codename.Contains(search.Query)));

		if (search.Take is > 0)
		{
			query = query.Take(search.Take.Value);
		}

		return await query
			.OrderBy(directive => directive.Title)
			.ToListAsync(cancellationToken);
	}

	/// <inheritdoc />
	public async Task<Directive> CreateStandaloneAsync(string title, string? codename = null, string? requestedId = null, CancellationToken cancellationToken = default)
	{
		return await CreateInternalAsync(title, codename, null, requestedId, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<Directive> CreateFromParentAsync(string parentDirectiveId, string title, string? codename = null, string? requestedId = null, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectiveId);
		var parentExists = await context.Directives.AnyAsync(directive => directive.Id == parentDirectiveId, cancellationToken);
		if (!parentExists)
		{
			throw new InvalidOperationException($"Parent directive '{parentDirectiveId}' was not found.");
		}

		return await CreateInternalAsync(title, codename, parentDirectiveId, requestedId, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<Directive> UpdateAsync(string directiveId, DirectiveUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directiveId);
		ArgumentNullException.ThrowIfNull(update);

		var directive = await context.Directives.FirstOrDefaultAsync(item => item.Id == directiveId, cancellationToken)
			?? throw new InvalidOperationException($"Directive '{directiveId}' was not found.");

		var previous = Clone(directive);
		if (!string.IsNullOrWhiteSpace(update.Title))
		{
			directive.Title = update.Title;
		}

		if (!string.IsNullOrWhiteSpace(update.Codename))
		{
			directive.Codename = update.Codename;
		}

		if (!string.IsNullOrWhiteSpace(update.ParentDirectiveId))
		{
			directive.ParentDirectiveId = update.ParentDirectiveId;
		}

		if (update.Tags is not null)
		{
			directive.Tags = update.Tags.ToList();
		}

		if (update.Due is not null)
		{
			directive.Due = update.Due;
		}

		if (!string.IsNullOrWhiteSpace(update.AlternativeLoreDirectory))
		{
			directive.AlternativeLoreDirectory = update.AlternativeLoreDirectory;
		}

		if (update.StartDate is not null)
		{
			directive.StartDate = update.StartDate;
		}

		if (update.EndDate is not null)
		{
			directive.EndDate = update.EndDate;
		}

		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SaveDirectiveAsync(directive, previous, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"directive.update",
			subject: directive,
			details: new { previousTitle = previous.Title, previousStatus = previous.Status.ToString() },
			cancellationToken: cancellationToken);
		return directive;
	}

	/// <inheritdoc />
	public async Task<Directive> ShiftWorkflowAsync(string directiveId, DirectiveWorkflowShift shift, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directiveId);
		ArgumentNullException.ThrowIfNull(shift);

		var directive = await context.Directives.FirstOrDefaultAsync(item => item.Id == directiveId, cancellationToken)
			?? throw new InvalidOperationException($"Directive '{directiveId}' was not found.");

		var previous = Clone(directive);
		directive.Status = shift.Status;
		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SaveDirectiveAsync(directive, previous, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"directive.workflow-shift",
			subject: directive,
			details: new { from = previous.Status.ToString(), to = directive.Status.ToString() },
			cancellationToken: cancellationToken);
		return directive;
	}

	/// <inheritdoc />
	public async Task DeleteAsync(string directiveId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directiveId);
		var directive = await context.Directives
			.Include(item => item.Subdirectives)
			.Include(item => item.Objectives)
			.FirstOrDefaultAsync(item => item.Id == directiveId, cancellationToken)
			?? throw new InvalidOperationException($"Directive '{directiveId}' was not found.");

		if (directive.Subdirectives.Count > 0 || directive.Objectives.Count > 0)
		{
			throw new InvalidOperationException("Directive cannot be deleted while it still has subdirectives or objectives.");
		}

		var snapshot = Clone(directive);
		var databaseGraveyard = await temporalDataService.ArchiveEntityAsync(snapshot, "api-delete", Environment.UserName, cancellationToken);
		context.Directives.Remove(directive);
		await context.SaveChangesAsync(cancellationToken);
		var fileGraveyard = await markdownFileService.DeleteDirectiveAsync(snapshot, cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"directive.delete",
			subjectType: nameof(Directive),
			subjectId: snapshot.Id,
			subjectTitle: snapshot.Title,
			temporalKind: "database",
			temporalEntryKey: databaseGraveyard.EntryKey,
			temporalEntityType: databaseGraveyard.EntityType,
			temporalEntityId: databaseGraveyard.EntityId,
			temporalEntityTitle: databaseGraveyard.EntityTitle,
			temporalLocation: fileGraveyard?.ArchivedRelativePath,
			details: new { fileTemporalEntryKey = fileGraveyard?.EntryKey },
			cancellationToken: cancellationToken);
	}

	private async Task<Directive> CreateInternalAsync(string title, string? codename, string? parentDirectiveId, string? requestedId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(title);

		var directive = new Directive
		{
			Id = puckCreationService.CreateIdFor<Directive>(requestedId),
			Title = title,
			Codename = codename,
			ParentDirectiveId = parentDirectiveId,
		};

		context.Directives.Add(directive);
		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SaveDirectiveAsync(directive, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "directive.create", subject: directive, cancellationToken: cancellationToken);
		return directive;
	}

	private static Directive Clone(Directive directive)
	{
		return new Directive
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
		};
	}
}