using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Plaintorch.Media;
using Pleiades.Plaintorch.State;
using Pleiades.Vault.Database;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Media;
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
	VaultAuditLogService auditLogService,
	DependencyGateService dependencyGate,
	VaultMediaService mediaService,
	MediaAssetFolderResolver folderResolver,
	VaultEntityGateway entityGateway) : IDirectiveApi
{
	/// <inheritdoc />
	public async Task<Directive?> GetAsync(string directiveId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directiveId);
		var directive = await context.Directives
			.AsNoTracking()
			.FirstOrDefaultAsync(item => item.Id == directiveId, cancellationToken);
		if (directive is not null)
		{
			await EnrichMediaAsync(directive, cancellationToken);
		}

		return directive;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Directive>> ListAsync(DirectiveKind? kind = null, CancellationToken cancellationToken = default)
	{
		var query = context.Directives.AsNoTracking().AsQueryable();
		query = kind switch
		{
			DirectiveKind.Stellar => query.Where(directive => directive is StellarDirective),
			DirectiveKind.Lunar => query.Where(directive => directive is LunarDirective),
			_ => query,
		};

		var directives = await query
			.OrderBy(directive => directive.Title)
			.ToListAsync(cancellationToken);
		await EnrichMediaAsync(directives, cancellationToken);
		return directives;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<StellarDirective>> ListStellarAsync(CancellationToken cancellationToken = default)
	{
		var directives = await context.Directives
			.AsNoTracking()
			.OfType<StellarDirective>()
			.OrderBy(directive => directive.Title)
			.ToListAsync(cancellationToken);
		await EnrichMediaAsync(directives, cancellationToken);
		return directives;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<LunarDirective>> ListLunarAsync(CancellationToken cancellationToken = default)
	{
		var directives = await context.LunarDirectives
			.AsNoTracking()
			.OrderBy(directive => directive.Title)
			.ToListAsync(cancellationToken);
		await EnrichMediaAsync(directives, cancellationToken);
		return directives;
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

		var directives = await query
			.OrderBy(directive => directive.Title)
			.ToListAsync(cancellationToken);
		await EnrichMediaAsync(directives, cancellationToken);
		return directives;
	}

	/// <inheritdoc />
	public async Task<StellarDirective> CreateStandaloneAsync(string title, string? codename = null, string? requestedId = null, CancellationToken cancellationToken = default)
	{
		return await CreateInternalAsync(title, codename, null, requestedId, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<StellarDirective> CreateFromParentAsync(string parentDirectiveId, string title, string? codename = null, string? requestedId = null, CancellationToken cancellationToken = default)
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
	public async Task<StellarDirective> UpdateStellarAsync(string directiveId, StellarDirectiveUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directiveId);
		ArgumentNullException.ThrowIfNull(update);

		var directive = await context.Directives.FirstOrDefaultAsync(item => item.Id == directiveId, cancellationToken)
			?? throw new InvalidOperationException($"Directive '{directiveId}' was not found.");
		if (directive is not StellarDirective stellar)
		{
			throw new InvalidOperationException($"Directive '{directiveId}' is a lunar directive; update it through the lunar update action instead.");
		}

		var previous = (StellarDirective)Clone(stellar);
		if (!string.IsNullOrWhiteSpace(update.Title))
		{
			stellar.Title = update.Title;
		}

		if (!string.IsNullOrWhiteSpace(update.Codename))
		{
			stellar.Codename = update.Codename;
		}

		if (!string.IsNullOrWhiteSpace(update.ParentDirectiveId))
		{
			stellar.ParentDirectiveId = update.ParentDirectiveId;
		}

		if (update.Tags is not null)
		{
			stellar.Tags = update.Tags.ToList();
		}

		if (update.Due is not null)
		{
			stellar.Due = update.Due;
		}

		if (update.StartDate is not null)
		{
			stellar.StartDate = update.StartDate;
		}

		if (update.EndDate is not null)
		{
			stellar.EndDate = update.EndDate;
		}

		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SaveDirectiveAsync(stellar, previous, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"directive.update-stellar",
			subject: stellar,
			details: new { previousTitle = previous.Title, previousStatus = previous.Status.ToString() },
			cancellationToken: cancellationToken);
		await EnrichMediaAsync(stellar, cancellationToken);
		return stellar;
	}

	/// <inheritdoc />
	public async Task<LunarDirective> UpdateLunarAsync(string directiveId, LunarDirectiveUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directiveId);
		ArgumentNullException.ThrowIfNull(update);

		var directive = await context.Directives.FirstOrDefaultAsync(item => item.Id == directiveId, cancellationToken)
			?? throw new InvalidOperationException($"Directive '{directiveId}' was not found.");
		if (directive is not LunarDirective lunar)
		{
			throw new InvalidOperationException($"Directive '{directiveId}' is a stellar directive; update it through the stellar update action instead.");
		}

		var previous = (LunarDirective)Clone(lunar);
		if (!string.IsNullOrWhiteSpace(update.Title))
		{
			lunar.Title = update.Title;
		}

		if (!string.IsNullOrWhiteSpace(update.Codename))
		{
			lunar.Codename = update.Codename;
		}

		if (!string.IsNullOrWhiteSpace(update.ParentDirectiveId))
		{
			lunar.ParentDirectiveId = update.ParentDirectiveId;
		}

		if (update.Tags is not null)
		{
			lunar.Tags = update.Tags.ToList();
		}

		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SaveDirectiveAsync(lunar, previous, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"directive.update-lunar",
			subject: lunar,
			details: new { previousTitle = previous.Title, previousStatus = previous.Status.ToString() },
			cancellationToken: cancellationToken);
		await EnrichMediaAsync(lunar, cancellationToken);
		return lunar;
	}

	/// <inheritdoc />
	public async Task<StellarDirective> ShiftStellarWorkflowAsync(string directiveId, StellarDirectiveWorkflowShift shift, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directiveId);
		ArgumentNullException.ThrowIfNull(shift);

		var directive = await context.Directives.FirstOrDefaultAsync(item => item.Id == directiveId, cancellationToken)
			?? throw new InvalidOperationException($"Directive '{directiveId}' was not found.");
		if (directive is not StellarDirective stellar)
		{
			throw new InvalidOperationException($"Directive '{directiveId}' is a lunar directive; shift its moonlight state through the lunar workflow instead.");
		}

		await dependencyGate.EnsureCanTransitionAsync(new EndpointRef(DependencyEndpointKind.Directive, stellar.Id), shift.Status, cancellationToken);

		var previousStatus = stellar.Status;
		stellar.Status = shift.Status;
		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SaveDirectiveAsync(stellar, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"directive.workflow-shift",
			subject: stellar,
			details: new { from = previousStatus.ToString(), to = stellar.Status.ToString() },
			cancellationToken: cancellationToken);
		await EnrichMediaAsync(stellar, cancellationToken);
		return stellar;
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
	public async Task<Directive> SetIconAsync(string directiveId, DirectiveIconRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directiveId);
		ArgumentNullException.ThrowIfNull(request);

		var directive = await context.Directives.FirstOrDefaultAsync(item => item.Id == directiveId, cancellationToken)
			?? throw new InvalidOperationException($"Directive '{directiveId}' was not found.");

		var previous = directive.Icon;
		await ApplyMediaChangeAsync(directiveId, directive, previous, request.Reference, request.Clear, key => directive.Icon = key, "directive.icon", cancellationToken);

		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SaveDirectiveAsync(directive, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"directive.set-icon",
			subject: directive,
			details: new { previous, icon = directive.Icon },
			cancellationToken: cancellationToken);
		await EnrichMediaAsync(directive, cancellationToken);
		return directive;
	}

	/// <inheritdoc />
	public async Task<Directive> SetBannerAsync(string directiveId, DirectiveBannerRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directiveId);
		ArgumentNullException.ThrowIfNull(request);

		var directive = await context.Directives.FirstOrDefaultAsync(item => item.Id == directiveId, cancellationToken)
			?? throw new InvalidOperationException($"Directive '{directiveId}' was not found.");

		var previous = directive.Banner;
		await ApplyMediaChangeAsync(directiveId, directive, previous, request.Reference, request.Clear, key => directive.Banner = key, "directive.banner", cancellationToken);

		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SaveDirectiveAsync(directive, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"directive.set-banner",
			subject: directive,
			details: new { previous, banner = directive.Banner },
			cancellationToken: cancellationToken);
		await EnrichMediaAsync(directive, cancellationToken);
		return directive;
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

		var previousStatus = lunar.Status;
		lunar.Status = shift.Status;
		await context.SaveChangesAsync(cancellationToken);
		await markdownFileService.SaveDirectiveAsync(lunar, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"directive.lunar-workflow-shift",
			subject: lunar,
			details: new { from = previousStatus.ToString(), to = lunar.Status.ToString() },
			cancellationToken: cancellationToken);
		return lunar;
	}

	/// <inheritdoc />
	public async Task<Timeframe> CreateTimeframeAsync(string lunarDirectiveId, TimeframePlan plan, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(lunarDirectiveId);
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentException.ThrowIfNullOrWhiteSpace(plan.Title);

		// Timeframes belong exclusively to lunar directives (PEP100).
		var directive = await context.Directives.AsNoTracking().FirstOrDefaultAsync(item => item.Id == lunarDirectiveId, cancellationToken)
			?? throw new InvalidOperationException($"Directive '{lunarDirectiveId}' was not found.");
		if (directive is not LunarDirective)
		{
			throw new InvalidOperationException($"Directive '{lunarDirectiveId}' is a stellar directive; only lunar directives can define timeframes.");
		}

		PlaintorchOrbitService.ValidateTimeframeOrbit(plan.Orbit);
		var timeframe = new Timeframe
		{
			DirectiveId = lunarDirectiveId,
			Title = plan.Title,
			StartTime = plan.StartTime,
			EndTime = plan.EndTime,
			Orbit = plan.Orbit,
			Icon = string.IsNullOrWhiteSpace(plan.Icon) ? null : plan.Icon.Trim(),
			AutoInclusion = plan.AutoInclusion,
			AutoInclusionCollege = plan.AutoInclusion == TimeframeInclusion.College ? plan.AutoInclusionCollege : null,
		};

		context.Timeframes.Add(timeframe);
		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"directive.create-timeframe",
			subjectType: nameof(Timeframe),
			subjectId: timeframe.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
			subjectTitle: timeframe.Title,
			details: new { lunarDirectiveId },
			cancellationToken: cancellationToken);
		mediaService.EnrichMedia(timeframe, null);
		return timeframe;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Timeframe>> ListTimeframesAsync(string lunarDirectiveId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(lunarDirectiveId);
		var timeframes = await context.Timeframes
			.AsNoTracking()
			.Where(item => item.DirectiveId == lunarDirectiveId)
			.OrderBy(item => item.StartTime)
			.ToListAsync(cancellationToken);
		foreach (var timeframe in timeframes)
		{
			mediaService.EnrichMedia(timeframe, null);
		}

		return timeframes;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<DirectiveTimeframeRecord>> ListAllTimeframesAsync(CancellationToken cancellationToken = default)
	{
		// Order on the entity columns before projecting into the record so the query stays SQL-translatable.
		var records = await context.Timeframes
			.AsNoTracking()
			.Join(
				context.LunarDirectives.AsNoTracking(),
				timeframe => timeframe.DirectiveId,
				directive => directive.Id,
				(timeframe, directive) => new { Timeframe = timeframe, Directive = directive })
			.OrderBy(pair => pair.Directive.Title)
			.ThenBy(pair => pair.Timeframe.StartTime)
			.Select(pair => new DirectiveTimeframeRecord(
				pair.Timeframe.Id,
				pair.Directive.Id,
				pair.Directive.Title,
				pair.Directive.Codename,
				pair.Directive.Status,
				pair.Timeframe.Title,
				pair.Timeframe.StartTime,
				pair.Timeframe.EndTime,
				pair.Timeframe.Orbit,
				pair.Timeframe.Icon,
				pair.Timeframe.AutoInclusion,
				pair.Timeframe.AutoInclusionCollege))
			.ToListAsync(cancellationToken);

		// The projection cannot call the media service, so resolve each icon companion afterwards. Timeframes
		// keep no self folder, so vault and glyph keys resolve and self keys carry no path.
		foreach (var record in records)
		{
			record.IconMedia = mediaService.ResolveReference(record.Icon, null);
		}

		return records;
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

		if (update.Icon is not null)
		{
			timeframe.Icon = string.IsNullOrWhiteSpace(update.Icon) ? null : update.Icon.Trim();
		}

		if (update.ClearIcon)
		{
			timeframe.Icon = null;
		}

		if (update.AutoInclusion is not null)
		{
			timeframe.AutoInclusion = update.AutoInclusion.Value;
			// Dropping to a non-college kind leaves no college behind to match against.
			if (update.AutoInclusion.Value != TimeframeInclusion.College)
			{
				timeframe.AutoInclusionCollege = null;
			}
		}

		if (update.AutoInclusionCollege is not null)
		{
			timeframe.AutoInclusionCollege = update.AutoInclusionCollege;
		}

		if (update.ClearAutoInclusionCollege)
		{
			timeframe.AutoInclusionCollege = null;
		}

		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"directive.update-timeframe",
			subjectType: nameof(Timeframe),
			subjectId: timeframe.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
			subjectTitle: timeframe.Title,
			cancellationToken: cancellationToken);
		mediaService.EnrichMedia(timeframe, null);
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

	private async Task<StellarDirective> CreateInternalAsync(string title, string? codename, string? parentDirectiveId, string? requestedId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(title);

		var directive = new StellarDirective
		{
			Id = puckCreationService.CreateIdFor<StellarDirective>(requestedId),
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

	private Directive Clone(Directive directive)
	{
		return (Directive)entityGateway.CloneScalars(directive);
	}

	/// <summary>
	/// Fills a directive's <c>[Media]</c> companions (<see cref="Directive.IconMedia"/> /
	/// <see cref="Directive.BannerMedia"/>) from its stored keys (PEP105), model-agnostically via
	/// <see cref="VaultMediaService.EnrichMedia"/>. The entity's own asset folder is resolved only when a
	/// <c>media:</c> (self) key is present, so glyph-only, vault-only, and no-media directives touch neither the
	/// database nor the filesystem.
	/// </summary>
	private async Task EnrichMediaAsync(Directive directive, CancellationToken cancellationToken)
	{
		var selfAssetFolder = mediaService.HasSelfMedia(directive)
			? await ResolveAssetFolderAsync(directive.Id, cancellationToken)
			: null;
		mediaService.EnrichMedia(directive, selfAssetFolder);
	}

	private async Task EnrichMediaAsync(IEnumerable<Directive> directives, CancellationToken cancellationToken)
	{
		foreach (var directive in directives)
		{
			await EnrichMediaAsync(directive, cancellationToken);
		}
	}

	/// <summary>
	/// Selects a media key for one field (PEP105): sets a raw reference, or clears — then archives the previously
	/// stored self image when the change leaves it orphaned. Storing an uploaded image is the media domain's job
	/// (<see cref="Pleiades.Plaintorch.Api.Abstractions.IMediaApi"/>); this only ever assigns a key.
	/// </summary>
	private async Task ApplyMediaChangeAsync(
		string directiveId,
		Directive directive,
		string? previousKey,
		string? reference,
		bool clear,
		Action<string?> setKey,
		string reasonPrefix,
		CancellationToken cancellationToken)
	{
		if (clear)
		{
			setKey(null);
			await ArchiveReplacedSelfMediaAsync(directiveId, previousKey, null, directive, $"{reasonPrefix}-clear", cancellationToken);
		}
		else if (!string.IsNullOrWhiteSpace(reference))
		{
			var newKey = reference.Trim();
			setKey(newKey);
			await ArchiveReplacedSelfMediaAsync(directiveId, previousKey, newKey, directive, $"{reasonPrefix}-reference", cancellationToken);
		}
		else
		{
			throw new InvalidOperationException("Media selection must supply a reference or clear.");
		}
	}

	/// <summary>
	/// Archives the file a replaced/cleared key pointed at, but only when it was <c>media:</c> (self) — self media
	/// is private to the directive and safe to reclaim, whereas a <c>vault:</c> file is shared and a glyph has no
	/// file. A key unchanged by the operation is left in place.
	/// </summary>
	private async Task ArchiveReplacedSelfMediaAsync(string directiveId, string? previousKey, string? newKey, Directive directive, string reason, CancellationToken cancellationToken)
	{
		if (string.Equals(previousKey, newKey, StringComparison.Ordinal))
		{
			return;
		}

		var (kind, file) = VaultMediaService.ParseKey(previousKey);
		if (kind != MediaKind.Media || string.IsNullOrWhiteSpace(file))
		{
			return;
		}

		var assetFolder = await ResolveAssetFolderAsync(directiveId, cancellationToken);
		if (assetFolder is not null)
		{
			await mediaService.DeleteAsync(assetFolder, file, reason, nameof(Directive), directive.Id, directive.Title, cancellationToken);
		}
	}

	/// <summary>
	/// Resolves a directive's own asset folder, used only to archive a self image the field stops pointing at. The
	/// composition itself lives in <see cref="MediaAssetFolderResolver"/>, shared with the media domain.
	/// </summary>
	private Task<string?> ResolveAssetFolderAsync(string directiveId, CancellationToken cancellationToken)
		=> folderResolver.ResolveAsync("directive", directiveId, cancellationToken);
}