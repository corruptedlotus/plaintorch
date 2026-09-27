using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Plaintorch.Materialization;
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
	VaultWriteQueue writeQueue,
	VaultEntityLifecycleService lifecycleService,
	VaultTemporalDataService temporalDataService,
	VaultAuditLogService auditLogService,
	DependencyGateService dependencyGate,
	VaultMediaService mediaService,
	MediaAssetFolderResolver folderResolver,
	VaultEntityGateway entityGateway,
	TimeframeCandidateService candidateService) : IDirectiveApi
{
	/// <inheritdoc />
	public async Task<Directive?> GetAsync(string directiveId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directiveId);
		var directive = await context.Directives
			.AsNoTracking()
			.FirstOrDefaultAsync(item => item.Id == directiveId, cancellationToken);
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
		return directives;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<LunarDirective>> ListLunarAsync(CancellationToken cancellationToken = default)
	{
		var directives = await context.LunarDirectives
			.AsNoTracking()
			.OrderBy(directive => directive.Title)
			.ToListAsync(cancellationToken);
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

		if (update.Codename.IsSet)
		{
			stellar.Codename = string.IsNullOrWhiteSpace(update.Codename.Value) ? null : update.Codename.Value;
		}

		if (update.ParentDirectiveId.IsSet)
		{
			// A present parent is applied: a value reparents, an explicit null lifts the directive to the top level.
			var parentId = string.IsNullOrWhiteSpace(update.ParentDirectiveId.Value) ? null : update.ParentDirectiveId.Value;
			if (parentId is not null)
			{
				await EnsureCanReparentAsync(stellar, parentId, cancellationToken);
			}

			stellar.ParentDirectiveId = parentId;
		}

		if (update.Tags is not null)
		{
			stellar.Tags = update.Tags.ToList();
		}

		if (update.Due.IsSet)
		{
			stellar.Due = update.Due.Value;
		}

		if (update.StartDate.IsSet)
		{
			stellar.StartDate = update.StartDate.Value;
		}

		if (update.EndDate.IsSet)
		{
			stellar.EndDate = update.EndDate.Value;
		}

		await writeQueue.WriteAsync(stellar, previous, cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"directive.update-stellar",
			subject: stellar,
			details: new { previousTitle = previous.Title, previousStatus = previous.Status.ToString() },
			cancellationToken: cancellationToken);
		return stellar;
	}

	/// <summary>
	/// Refuses a new parent that would break the directive tree: one that does not exist, one of the other family
	/// (a lunar hierarchy stays lunar, a stellar one stellar — PEP100), the directive itself, or one of its own
	/// descendants, which would close the parent chain into a loop no root ever reaches.
	/// </summary>
	private async Task EnsureCanReparentAsync(Directive directive, string parentDirectiveId, CancellationToken cancellationToken)
	{
		if (string.Equals(directive.ParentDirectiveId, parentDirectiveId, StringComparison.Ordinal))
		{
			return;
		}

		if (string.Equals(directive.Id, parentDirectiveId, StringComparison.Ordinal))
		{
			throw new InvalidOperationException($"Directive '{directive.Id}' cannot be its own parent.");
		}

		var parents = await context.Directives
			.AsNoTracking()
			.Select(item => new { item.Id, item.ParentDirectiveId, IsLunar = item is LunarDirective })
			.ToDictionaryAsync(item => item.Id, cancellationToken);
		if (!parents.TryGetValue(parentDirectiveId, out var parent))
		{
			throw new InvalidOperationException($"Directive '{parentDirectiveId}' was not found.");
		}

		if (parent.IsLunar != (directive is LunarDirective))
		{
			throw new InvalidOperationException("A lunar directive can only sit under a lunar directive, and a stellar one under a stellar one.");
		}

		// Walk up from the new parent; meeting the directive on the way means the parent is one of its descendants.
		// The vault is hand-editable, so the chain may already loop — the visited set keeps the walk finite.
		var visited = new HashSet<string>(StringComparer.Ordinal);
		for (var current = parent; current is not null && visited.Add(current.Id);)
		{
			if (string.Equals(current.ParentDirectiveId, directive.Id, StringComparison.Ordinal))
			{
				throw new InvalidOperationException($"Directive '{parentDirectiveId}' descends from '{directive.Id}' and cannot become its parent.");
			}

			current = current.ParentDirectiveId is not null && parents.TryGetValue(current.ParentDirectiveId, out var next) ? next : null;
		}
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

		if (update.Codename.IsSet)
		{
			lunar.Codename = string.IsNullOrWhiteSpace(update.Codename.Value) ? null : update.Codename.Value;
		}

		if (update.ParentDirectiveId.IsSet)
		{
			// A present parent is applied: a value reparents, an explicit null lifts the directive to the top level.
			var parentId = string.IsNullOrWhiteSpace(update.ParentDirectiveId.Value) ? null : update.ParentDirectiveId.Value;
			if (parentId is not null)
			{
				await EnsureCanReparentAsync(lunar, parentId, cancellationToken);
			}

			lunar.ParentDirectiveId = parentId;
		}

		if (update.Tags is not null)
		{
			lunar.Tags = update.Tags.ToList();
		}

		await writeQueue.WriteAsync(lunar, previous, cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"directive.update-lunar",
			subject: lunar,
			details: new { previousTitle = previous.Title, previousStatus = previous.Status.ToString() },
			cancellationToken: cancellationToken);
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
		await writeQueue.WriteAsync(stellar, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"directive.workflow-shift",
			subject: stellar,
			details: new { from = previousStatus.ToString(), to = stellar.Status.ToString() },
			cancellationToken: cancellationToken);
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
		var databaseGraveyard = temporalDataService.StageEntityArchive(snapshot, "api-delete", Environment.UserName);
		// A lunar directive's timeframes go by the database cascade; the state-policy save hook clears the availabilities
		// pointing at them, tracked, in this same save (PEP100 patch 2, D9).
		context.Directives.Remove(directive);
		await writeQueue.RecordRemoveAsync(snapshot, cancellationToken);
		await context.SaveChangesAsync(cancellationToken);
		var fileGraveyard = await writeQueue.DrainRemoveAsync(snapshot, cancellationToken);
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

		await writeQueue.WriteAsync(directive, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"directive.set-icon",
			subject: directive,
			details: new { previous, icon = directive.Icon },
			cancellationToken: cancellationToken);
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

		await writeQueue.WriteAsync(directive, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"directive.set-banner",
			subject: directive,
			details: new { previous, banner = directive.Banner },
			cancellationToken: cancellationToken);
		return directive;
	}

	/// <inheritdoc />
	public async Task<Directive> SetAvailabilityAsync(string directiveId, long? timeframeId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directiveId);

		var directive = await context.Directives.FirstOrDefaultAsync(item => item.Id == directiveId, cancellationToken)
			?? throw new InvalidOperationException($"Directive '{directiveId}' was not found.");

		if (timeframeId is { } requestedId)
		{
			var inclusion = await context.Timeframes
				.AsNoTracking()
				.Where(item => item.Id == requestedId)
				.Select(item => (TimeframeInclusion?)item.AutoInclusion)
				.FirstOrDefaultAsync(cancellationToken)
				?? throw new InvalidOperationException($"Timeframe '{requestedId}' was not found.");
			if (inclusion != TimeframeInclusion.Availability)
			{
				throw new InvalidOperationException($"Timeframe '{requestedId}' is not an availability timeframe; switch its auto-inclusion to Availability first.");
			}
		}

		var previous = directive.AvailabilityTimeframeId;
		directive.AvailabilityTimeframeId = timeframeId;

		// Availability is database-only (never in frontmatter), so a plain save is enough — there is no note to rewrite.
		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"directive.set-availability",
			subject: directive,
			details: new { previous, availabilityTimeframeId = directive.AvailabilityTimeframeId },
			cancellationToken: cancellationToken);
		return directive;
	}

	/// <inheritdoc />
	// Directive init (Freeform) is a policy-derived action: it delegates to the one generic init dispatched through the
	// mode policy (REFACTOR Alpha phase 5a). The directive-specific pieces it used to carry — the init-path fallback and
	// the manual-no-PUCK override — are now the generic init's declared behaviour.
	public async Task<Directive> InitializeFromPathAsync(string vaultRelativePath, CancellationToken cancellationToken = default)
		=> (Directive)await lifecycleService.InitializeFromFileAsync(typeof(Directive), vaultRelativePath, cancellationToken);

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
		await writeQueue.WriteAsync(directive, cancellationToken: cancellationToken);
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
		await writeQueue.WriteAsync(lunar, cancellationToken: cancellationToken);
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
			AutoInclusionColleges = plan.AutoInclusion == TimeframeInclusion.College ? (plan.AutoInclusionColleges?.ToList() ?? []) : [],
			Exclusive = plan.Exclusive,
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
		return timeframes;
	}

	/// <inheritdoc />
	public Task<IReadOnlyList<DirectiveTimeframeRecord>> ListAllTimeframesAsync(CancellationToken cancellationToken = default)
		=> ProjectTimeframeRecordsAsync(null, cancellationToken);

	/// <inheritdoc />
	public async Task<IReadOnlyList<DirectiveTimeframeRecord>> ListActiveTimeframesAsync(DateTimeOffset? at = null, CancellationToken cancellationToken = default)
	{
		// Strictly active only: a cycle that has not begun, or has ended, shows no active timeframes (never the
		// any-state "today's cycle" fallback other readers use).
		var activeCycle = await context.PolarisCycles
			.AsNoTracking()
			.Where(cycle => cycle.StartTime != null && cycle.EndTime == null)
			.OrderByDescending(cycle => cycle.Id)
			.FirstOrDefaultAsync(cancellationToken);
		if (activeCycle is null)
		{
			return [];
		}

		var candidateIds = await candidateService.GetCandidateIdsAsync(activeCycle, cancellationToken);
		if (candidateIds.Count == 0)
		{
			return [];
		}

		var clock = TimeOnly.FromDateTime((at ?? DateTimeOffset.Now).LocalDateTime);
		var minute = MinuteOfDay(clock);
		var active = (await ProjectTimeframeRecordsAsync(candidateIds, cancellationToken))
			.Where(record => IsActiveAt(record, minute))
			.ToList();

		// Exclusivity is judged among the timeframes active right now: any active exclusive one suppresses every active
		// non-exclusive one, and all the active exclusive ones are kept.
		return active.Any(record => record.Exclusive)
			? active.Where(record => record.Exclusive).ToList()
			: active;
	}

	/// <summary>
	/// Whether a timeframe's window covers the given minute of the day (PEP100 patch 2): <c>[start, end)</c> at minute
	/// precision; a start after the end wraps midnight; a start equal to the end is never active.
	/// </summary>
	private static bool IsActiveAt(DirectiveTimeframeRecord record, int minute)
	{
		var start = MinuteOfDay(record.StartTime);
		var end = MinuteOfDay(record.EndTime);
		return start < end
			? minute >= start && minute < end
			: start > end && (minute >= start || minute < end);
	}

	private static int MinuteOfDay(TimeOnly time) => (time.Hour * 60) + time.Minute;

	/// <summary>
	/// Projects timeframes joined to their lunar directive into records, ordered by directive title then start time —
	/// every timeframe, or only those in <paramref name="ids"/>.
	/// </summary>
	private async Task<IReadOnlyList<DirectiveTimeframeRecord>> ProjectTimeframeRecordsAsync(IReadOnlyCollection<long>? ids, CancellationToken cancellationToken)
	{
		var timeframes = context.Timeframes.AsNoTracking();
		if (ids is not null)
		{
			timeframes = timeframes.Where(timeframe => ids.Contains(timeframe.Id));
		}

		// The colleges are a JSON list column that cannot be projected in SQL, so the joined rows are materialized
		// (ordered on entity columns first) and mapped to records in memory.
		var pairs = await timeframes
			.Join(
				context.LunarDirectives.AsNoTracking(),
				timeframe => timeframe.DirectiveId,
				directive => directive.Id,
				(timeframe, directive) => new { Timeframe = timeframe, Directive = directive })
			.OrderBy(pair => pair.Directive.Title)
			.ThenBy(pair => pair.Timeframe.StartTime)
			.ToListAsync(cancellationToken);

		var records = pairs
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
				pair.Timeframe.AutoInclusionColleges,
				pair.Timeframe.Exclusive))
			.ToList();

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

		if (update.Orbit.IsSet)
		{
			var normalizedOrbit = string.IsNullOrWhiteSpace(update.Orbit.Value) ? null : update.Orbit.Value;
			PlaintorchOrbitService.ValidateTimeframeOrbit(normalizedOrbit);
			timeframe.Orbit = normalizedOrbit;
		}

		if (update.Icon.IsSet)
		{
			timeframe.Icon = string.IsNullOrWhiteSpace(update.Icon.Value) ? null : update.Icon.Value.Trim();
		}

		// Leaving Availability mode clears the directives pointing at this timeframe in the state-policy save hook
		// (PEP100 patch 2, D9), on every write pathway.
		if (update.AutoInclusion is not null)
		{
			timeframe.AutoInclusion = update.AutoInclusion.Value;
			// Dropping to a non-college kind leaves no colleges behind to match against.
			if (update.AutoInclusion.Value != TimeframeInclusion.College)
			{
				timeframe.AutoInclusionColleges = [];
			}
		}

		// A null college list leaves it unchanged; any list (empty included) replaces it.
		if (update.AutoInclusionColleges is not null)
		{
			timeframe.AutoInclusionColleges = update.AutoInclusionColleges.ToList();
		}

		if (update.Exclusive is not null)
		{
			timeframe.Exclusive = update.Exclusive.Value;
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

		// The state-policy save hook clears the directive availabilities pointing at it, tracked (PEP100 patch 2, D9).
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
		await writeQueue.WriteAsync(directive, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "directive.create", subject: directive, cancellationToken: cancellationToken);
		return directive;
	}

	private Directive Clone(Directive directive)
	{
		return (Directive)entityGateway.CloneScalars(directive);
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