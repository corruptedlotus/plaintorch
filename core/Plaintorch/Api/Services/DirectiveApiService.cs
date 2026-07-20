using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Vault.Database;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Watcher;
using Pleiades.Vault;

namespace Pleiades.Plaintorch.Api.Services;

/// <summary>
/// Implements the directive-facing PLAINTORCH application API.
/// </summary>
public sealed class DirectiveApiService(
	PlainfraContext context,
	PuckCreationService puckCreationService,
	PlaintorchMarkdownStorageService markdownFileService,
	VaultLayout layout,
	VaultMarkdownDiscoveryService watcherDiscoveryService,
	VaultWatcherSyncService watcherSyncService,
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
		if (directive is LunarDirective)
		{
			throw new InvalidOperationException($"Directive '{directiveId}' is a lunar directive; shift its moonlight state through the lunar workflow instead.");
		}

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
			.Include(item => item.Incentives)
			.FirstOrDefaultAsync(item => item.Id == directiveId, cancellationToken)
			?? throw new InvalidOperationException($"Directive '{directiveId}' was not found.");

		if (directive.Subdirectives.Count > 0 || directive.Incentives.Count > 0)
		{
			throw new InvalidOperationException("Directive cannot be deleted while it still has subdirectives or incentives.");
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

	/// <inheritdoc />
	public async Task<Directive> InitializeFromPathAsync(string vaultRelativePath, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(vaultRelativePath);
		var normalizedRelativePath = vaultRelativePath
			.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
			.TrimStart(Path.DirectorySeparatorChar);
		var absolutePath = Path.GetFullPath(Path.Combine(layout.VaultRoot, normalizedRelativePath));
		var normalizedRoot = layout.VaultRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

		if (!absolutePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Directive init path escapes the active vault root.");
		}

		var candidate = await watcherDiscoveryService.InspectPathAsync(absolutePath, "api-init", cancellationToken)
			?? await watcherDiscoveryService.InspectDirectiveInitPathAsync(absolutePath, "api-init-fallback", cancellationToken)
			?? throw new InvalidOperationException($"Directive init path '{normalizedRelativePath}' is not a directive markdown candidate eligible for freeform initialization.");

		if (candidate.Model.EntityType != typeof(Directive))
		{
			throw new InvalidOperationException($"Directive init path '{normalizedRelativePath}' resolved to '{candidate.Model.EntityName}', not Directive.");
		}

		if (ShouldTreatAsManualFreeformInit(candidate))
		{
			candidate = NormalizeManualFreeformInitCandidate(candidate);
		}

		if (!candidate.IsValid)
		{
			var reasons = string.Join("; ", candidate.Issues.Select(issue => $"{issue.FieldPath}: {issue.Message}"));
			throw new InvalidOperationException($"Directive init path '{normalizedRelativePath}' violates freeform init policy: {reasons}");
		}

		if (candidate.SuggestedAction != VaultSyncAction.CreateFromFile)
		{
			throw new InvalidOperationException(
				$"Directive init path '{normalizedRelativePath}' is not eligible for initialization. Suggested action '{candidate.SuggestedAction}' indicates this path must be handled by watcher reconciliation policy instead. Reason: {candidate.SuggestedReason ?? "n/a"}");
		}

		await watcherSyncService.InitializeFromFileAsync(candidate, "api-init", cancellationToken);
		var created = await context.Directives.FirstOrDefaultAsync(item => item.Id == ((PuckNamedEntity)candidate.ParsedModel).Id, cancellationToken);
		if (created is null)
		{
			throw new InvalidOperationException("Directive initialization completed but the created entity could not be loaded.");
		}

		await auditLogService.WriteAsync("api", "directive.init", subject: created, details: new { path = normalizedRelativePath }, cancellationToken: cancellationToken);
		return created;
	}

	private static bool ShouldTreatAsManualFreeformInit(VaultSyncCandidate candidate)
	{
		if (candidate.Model.EntityType != typeof(Directive)
			|| candidate.Model.Mode != VaultStorageMode.Freeform
			|| candidate.SuggestedAction != VaultSyncAction.Ignore)
		{
			return false;
		}

		if (string.IsNullOrWhiteSpace(candidate.SuggestedReason)
			|| !candidate.SuggestedReason.Contains("does not auto-create entities from files without frontmatter PUCK identity", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		return candidate.Issues.All(IsMissingRequiredPuckInputIssue);
	}

	private static VaultSyncCandidate NormalizeManualFreeformInitCandidate(VaultSyncCandidate candidate)
	{
		var filteredIssues = candidate.Issues
			.Where(issue => !IsMissingRequiredPuckInputIssue(issue))
			.ToList();

		return candidate with
		{
			Issues = filteredIssues,
			SuggestedAction = VaultSyncAction.CreateFromFile,
			SuggestedReason = "Manual freeform directive initialization requested from API.",
		};
	}

	private static bool IsMissingRequiredPuckInputIssue(MarkdownValidationIssue issue)
	{
		return string.Equals(issue.FieldPath, "id", StringComparison.OrdinalIgnoreCase)
			&& issue.Message.Contains("missing required caller-provided PUCK input", StringComparison.OrdinalIgnoreCase);
	}

	/// <inheritdoc />
	public async Task<LunarDirective> CreateLunarAsync(string title, string? codename = null, string? parentDirectiveId = null, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(title);
		if (!string.IsNullOrWhiteSpace(parentDirectiveId))
		{
			var parentExists = await context.Directives.AnyAsync(directive => directive.Id == parentDirectiveId, cancellationToken);
			if (!parentExists)
			{
				throw new InvalidOperationException($"Parent directive '{parentDirectiveId}' was not found.");
			}
		}

		var directive = new LunarDirective
		{
			Id = puckCreationService.CreateIdFor<LunarDirective>(),
			Title = title,
			Codename = codename,
			ParentDirectiveId = parentDirectiveId,
		};

		context.Directives.Add(directive);
		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SaveDirectiveAsync(directive, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "directive.create-lunar", subject: directive, cancellationToken: cancellationToken);
		return directive;
	}

	/// <inheritdoc />
	public async Task<LunarDirective> ShiftLunarWorkflowAsync(string directiveId, LunarDirectiveWorkflowShift shift, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directiveId);
		ArgumentNullException.ThrowIfNull(shift);

		var directive = await context.Directives.FirstOrDefaultAsync(item => item.Id == directiveId, cancellationToken)
			?? throw new InvalidOperationException($"Directive '{directiveId}' was not found.");
		if (directive is not LunarDirective lunar)
		{
			throw new InvalidOperationException($"Directive '{directiveId}' is a stellar directive and has no moonlight state; use the regular workflow shift instead.");
		}

		var previousStatus = lunar.LunarStatus;
		lunar.LunarStatus = shift.Status;
		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SaveDirectiveAsync(lunar, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"directive.lunar-workflow-shift",
			subject: lunar,
			details: new { from = previousStatus.ToString(), to = lunar.LunarStatus.ToString() },
			cancellationToken: cancellationToken);
		return lunar;
	}

	/// <inheritdoc />
	public async Task<Timeframe> CreateTimeframeAsync(string directiveId, TimeframePlan plan, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directiveId);
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentException.ThrowIfNullOrWhiteSpace(plan.Title);

		var directiveExists = await context.Directives.AnyAsync(item => item.Id == directiveId, cancellationToken);
		if (!directiveExists)
		{
			throw new InvalidOperationException($"Directive '{directiveId}' was not found.");
		}

		PlaintorchOrbitService.ValidateTimeframeOrbit(plan.Orbit);
		var timeframe = new Timeframe
		{
			DirectiveId = directiveId,
			Title = plan.Title,
			StartTime = plan.StartTime,
			EndTime = plan.EndTime,
			Orbit = plan.Orbit,
		};

		context.Timeframes.Add(timeframe);
		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"directive.create-timeframe",
			subjectType: nameof(Timeframe),
			subjectId: timeframe.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
			subjectTitle: timeframe.Title,
			details: new { directiveId },
			cancellationToken: cancellationToken);
		return timeframe;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Timeframe>> ListTimeframesAsync(string directiveId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directiveId);
		return await context.Timeframes
			.AsNoTracking()
			.Where(item => item.DirectiveId == directiveId)
			.OrderBy(item => item.StartTime)
			.ToListAsync(cancellationToken);
	}

	/// <inheritdoc />
	public async Task<Timeframe> UpdateTimeframeAsync(long timeframeId, TimeframeUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(update);
		var timeframe = await context.Timeframes.FirstOrDefaultAsync(item => item.Id == timeframeId, cancellationToken)
			?? throw new InvalidOperationException($"Timeframe '{timeframeId}' was not found.");

		if (!string.IsNullOrWhiteSpace(update.Title))
		{
			timeframe.Title = update.Title;
		}

		if (update.StartTime is not null)
		{
			timeframe.StartTime = update.StartTime.Value;
		}

		if (update.EndTime is not null)
		{
			timeframe.EndTime = update.EndTime.Value;
		}

		if (update.Orbit is not null)
		{
			var normalizedOrbit = string.IsNullOrWhiteSpace(update.Orbit) ? null : update.Orbit;
			PlaintorchOrbitService.ValidateTimeframeOrbit(normalizedOrbit);
			timeframe.Orbit = normalizedOrbit;
		}

		if (update.ClearOrbit)
		{
			timeframe.Orbit = null;
		}

		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"directive.update-timeframe",
			subjectType: nameof(Timeframe),
			subjectId: timeframe.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
			subjectTitle: timeframe.Title,
			cancellationToken: cancellationToken);
		return timeframe;
	}

	/// <inheritdoc />
	public async Task DeleteTimeframeAsync(long timeframeId, CancellationToken cancellationToken = default)
	{
		var timeframe = await context.Timeframes.FirstOrDefaultAsync(item => item.Id == timeframeId, cancellationToken)
			?? throw new InvalidOperationException($"Timeframe '{timeframeId}' was not found.");

		context.Timeframes.Remove(timeframe);
		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"directive.delete-timeframe",
			subjectType: nameof(Timeframe),
			subjectId: timeframe.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
			subjectTitle: timeframe.Title,
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
			StartDate = directive.StartDate,
			EndDate = directive.EndDate,
		};
	}
}