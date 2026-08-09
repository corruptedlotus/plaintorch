using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Plaintorch.Materialization;
using Pleiades.Plaintorch.State;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Api.Services;

/// <summary>
/// Implements the declarative-facing PLAINTORCH application API (PEP100).
/// </summary>
/// <remarks>
/// Declaratives are event-like: they are never acted on directly. Interaction with a single occurrence
/// materializes its instance first (an eventive for fates, an attentive for decrees) and the change is then
/// applied to that instance. Instances created here are never Polaris-bound; binding only happens by
/// manually adding a decree to a Polaris cycle through the Polaris API. Interaction with an orbit-scheduled
/// occurrence is validated through preview (non-seeking) resolution so a future instance never pushes the
/// schedule state forward; its occurrence date is its identity, so the later seeking pass recognizes it.
/// </remarks>
public sealed class DeclarativeApiService(
	PlainfraContext context,
	PuckCreationService puckCreationService,
	PlaintorchMarkdownStorageService markdownStorageService,
	PlaintorchOrbitService orbitService,
	VaultTemporalDataService temporalDataService,
	DependencyGateService dependencyGate,
	ProximityMaterializationService materializationService,
	VaultAuditLogService auditLogService,
	ILogger<DeclarativeApiService> logger) : IDeclarativeApi
{
	/// <summary>
	/// Re-runs the day's materialization after a declarative changed, so a new or changed orbit's due
	/// instances (attentives today, eventives across the horizon) appear immediately rather than only on the
	/// next daily pass. Best-effort: the declarative is already committed and the daily pass is the backstop,
	/// so a materialization hiccup must not fail the create/update.
	/// </summary>
	private async Task RecheckMaterializationAsync(CancellationToken cancellationToken)
	{
		try
		{
			// Catch up the next 24h's due instances only. The recheck is about immediacy — a decree's attentive
			// or a fate's imminent eventive appearing the moment its orbit is set — while filling the upcoming
			// eventive horizon stays the rolling background pass's remit, so an edit does not front-run a week of
			// occurrences.
			await materializationService.MaterializeForNowAsync(
				DateTimeOffset.Now,
				eventiveHorizonDays: 0,
				cancellationToken);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception)
		{
			logger.LogWarning(exception, "Materialization recheck after a declarative change failed; the daily pass will retry.");
		}
	}

	/// <inheritdoc />
	public Task<Fate?> GetFateAsync(string fateId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fateId);
		return context.Fates
			.AsNoTracking()
			.FirstOrDefaultAsync(fate => fate.Id == fateId, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Fate>> ListFatesAsync(CancellationToken cancellationToken = default)
	{
		return await context.Fates
			.AsNoTracking()
			.OrderBy(fate => fate.Title)
			.ToListAsync(cancellationToken);
	}

	/// <inheritdoc />
	public async Task<Fate> CreateFateAsync(FatePlan plan, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentException.ThrowIfNullOrWhiteSpace(plan.Title);
		await EnsureDirectiveExistsAsync(plan.DirectiveId, cancellationToken);
		ValidateEventWindow(plan.StartTime, plan.EndTime);
		PlaintorchOrbitService.ValidateFateOrbit(plan.Orbit);

		var fate = new Fate
		{
			Id = puckCreationService.CreateIdFor<Fate>(plan.Id),
			Title = plan.Title,
			DirectiveId = plan.DirectiveId,
			Date = plan.Date,
			StartTime = plan.StartTime,
			EndTime = plan.EndTime,
			Orbit = plan.Orbit,
			EventDuration = plan.EventDuration,
		};

		if (!string.IsNullOrWhiteSpace(plan.ParentIncentiveId))
		{
			await ApplyParentAsync(fate, plan.ParentIncentiveId, cancellationToken);
		}

		context.Fates.Add(fate);
		await orbitService.ResetStateAsync(fate, fate.Orbit, DateOnly.FromDateTime(DateTime.Today), cancellationToken);
		await context.SaveChangesAsync(cancellationToken);
		await markdownStorageService.SaveFateAsync(fate, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "fate.create", subject: fate, cancellationToken: cancellationToken);
		await RecheckMaterializationAsync(cancellationToken);
		return fate;
	}

	/// <inheritdoc />
	public async Task<Fate> UpdateFateAsync(string fateId, FateUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fateId);
		ArgumentNullException.ThrowIfNull(update);

		var fate = await context.Fates.FirstOrDefaultAsync(item => item.Id == fateId, cancellationToken)
			?? throw new InvalidOperationException($"Fate '{fateId}' was not found.");
		var previous = CloneFate(fate);
		var previousOrbit = fate.Orbit;

		if (!string.IsNullOrWhiteSpace(update.Title))
		{
			fate.Title = update.Title;
		}

		if (update.Status is not null && update.Status.Value != fate.Status)
		{
			await dependencyGate.EnsureCanTransitionAsync(new EndpointRef(DependencyEndpointKind.Fate, fate.Id), update.Status.Value, cancellationToken);
			fate.Status = update.Status.Value;
		}

		if (!string.IsNullOrWhiteSpace(update.DirectiveId))
		{
			await EnsureDirectiveExistsAsync(update.DirectiveId, cancellationToken);
			fate.DirectiveId = update.DirectiveId;
		}

		if (!string.IsNullOrWhiteSpace(update.ParentIncentiveId))
		{
			await ApplyParentAsync(fate, update.ParentIncentiveId, cancellationToken);
		}

		if (update.ClearParentIncentive)
		{
			fate.ParentIncentiveId = null;
		}

		if (update.Date is not null)
		{
			fate.Date = update.Date;
		}

		if (update.StartTime is not null)
		{
			fate.StartTime = update.StartTime;
		}

		if (update.EndTime is not null)
		{
			fate.EndTime = update.EndTime;
		}

		if (update.Orbit is not null)
		{
			var normalizedOrbit = string.IsNullOrWhiteSpace(update.Orbit) ? null : update.Orbit;
			PlaintorchOrbitService.ValidateFateOrbit(normalizedOrbit);
			fate.Orbit = normalizedOrbit;
		}

		if (update.EventDuration is not null)
		{
			fate.EventDuration = update.EventDuration;
		}

		ValidateEventWindow(fate.StartTime, fate.EndTime);
		if (!string.Equals(previousOrbit, fate.Orbit, StringComparison.Ordinal))
		{
			await orbitService.ResetStateAsync(fate, fate.Orbit, DateOnly.FromDateTime(DateTime.Today), cancellationToken);
		}

		await context.SaveChangesAsync(cancellationToken);
		await markdownStorageService.SaveFateAsync(fate, previous, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "fate.update", subject: fate, cancellationToken: cancellationToken);
		await RecheckMaterializationAsync(cancellationToken);
		return fate;
	}

	/// <inheritdoc />
	public async Task<Fate> BeginFateBoundaryAsync(string fateId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fateId);
		var fate = await context.Fates.FirstOrDefaultAsync(item => item.Id == fateId, cancellationToken)
			?? throw new InvalidOperationException($"Fate '{fateId}' was not found.");

		await markdownStorageService.SaveFateAsync(fate, beginBoundary: true, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "fate.begin-boundary", subject: fate, cancellationToken: cancellationToken);
		return fate;
	}

	/// <inheritdoc />
	public async Task DeleteFateAsync(string fateId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fateId);
		var fate = await context.Fates
			.Include(item => item.ChildIncentives)
			.FirstOrDefaultAsync(item => item.Id == fateId, cancellationToken)
			?? throw new InvalidOperationException($"Fate '{fateId}' was not found.");

		if (fate.ChildIncentives.Count > 0)
		{
			throw new InvalidOperationException("Fate cannot be deleted while other incentives still name it as their parent.");
		}

		var graveyardEntry = await temporalDataService.ArchiveEntityAsync(fate, "api-delete", Environment.UserName, cancellationToken);
		context.Fates.Remove(fate);
		await context.SaveChangesAsync(cancellationToken);
		await markdownStorageService.DeleteFateAsync(fate, cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"fate.delete",
			subjectType: nameof(Fate),
			subjectId: fate.Id,
			subjectTitle: fate.Title,
			temporalKind: "database",
			temporalEntryKey: graveyardEntry.EntryKey,
			temporalEntityType: graveyardEntry.EntityType,
			temporalEntityId: graveyardEntry.EntityId,
			temporalEntityTitle: graveyardEntry.EntityTitle,
			cancellationToken: cancellationToken);
	}

	/// <inheritdoc />
	public Task<Decree?> GetDecreeAsync(string decreeId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(decreeId);
		return context.Decrees
			.AsNoTracking()
			.FirstOrDefaultAsync(decree => decree.Id == decreeId, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Decree>> ListDecreesAsync(CancellationToken cancellationToken = default)
	{
		return await context.Decrees
			.AsNoTracking()
			.OrderBy(decree => decree.Title)
			.ToListAsync(cancellationToken);
	}

	/// <inheritdoc />
	public async Task<Decree> CreateDecreeAsync(DecreePlan plan, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentException.ThrowIfNullOrWhiteSpace(plan.Title);
		await EnsureDirectiveExistsAsync(plan.DirectiveId, cancellationToken);
		PlaintorchOrbitService.ValidateDecreeOrbit(plan.Orbit, plan.Reflect);

		var decree = new Decree
		{
			Id = puckCreationService.CreateIdFor<Decree>(plan.Id),
			Title = plan.Title,
			DirectiveId = plan.DirectiveId,
			Orbit = plan.Orbit,
			DefaultLength = plan.DefaultLength,
			ActiveCelestron = plan.ActiveCelestron,
			Reflect = plan.Reflect,
		};

		context.Decrees.Add(decree);
		await orbitService.ResetStateAsync(decree, decree.Orbit, DateOnly.FromDateTime(DateTime.Today), cancellationToken);
		await context.SaveChangesAsync(cancellationToken);
		await markdownStorageService.SaveDecreeAsync(decree, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "decree.create", subject: decree, cancellationToken: cancellationToken);
		await RecheckMaterializationAsync(cancellationToken);
		return decree;
	}

	/// <inheritdoc />
	public async Task<Decree> UpdateDecreeAsync(string decreeId, DecreeUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(decreeId);
		ArgumentNullException.ThrowIfNull(update);

		var decree = await context.Decrees.FirstOrDefaultAsync(item => item.Id == decreeId, cancellationToken)
			?? throw new InvalidOperationException($"Decree '{decreeId}' was not found.");
		var previous = CloneDecree(decree);
		var previousOrbit = decree.Orbit;

		if (!string.IsNullOrWhiteSpace(update.Title))
		{
			decree.Title = update.Title;
		}

		if (update.Status is not null)
		{
			decree.Status = update.Status.Value;
		}

		if (!string.IsNullOrWhiteSpace(update.DirectiveId))
		{
			await EnsureDirectiveExistsAsync(update.DirectiveId, cancellationToken);
			decree.DirectiveId = update.DirectiveId;
		}

		if (update.Orbit is not null)
		{
			decree.Orbit = string.IsNullOrWhiteSpace(update.Orbit) ? null : update.Orbit;
		}

		if (update.DefaultLength is not null)
		{
			decree.DefaultLength = update.DefaultLength;
		}

		if (update.ActiveCelestron is not null)
		{
			decree.ActiveCelestron = update.ActiveCelestron.Value;
		}

		if (update.Reflect is not null)
		{
			decree.Reflect = update.Reflect.Value;
		}

		// Validate the resulting combination: reflecting decrees demand day-granularity orbits.
		PlaintorchOrbitService.ValidateDecreeOrbit(decree.Orbit, decree.Reflect);

		if (!string.Equals(previousOrbit, decree.Orbit, StringComparison.Ordinal))
		{
			await orbitService.ResetStateAsync(decree, decree.Orbit, DateOnly.FromDateTime(DateTime.Today), cancellationToken);
		}

		await context.SaveChangesAsync(cancellationToken);
		await markdownStorageService.SaveDecreeAsync(decree, previous, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "decree.update", subject: decree, cancellationToken: cancellationToken);
		await RecheckMaterializationAsync(cancellationToken);
		return decree;
	}

	/// <inheritdoc />
	public async Task<Decree> BeginDecreeBoundaryAsync(string decreeId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(decreeId);
		var decree = await context.Decrees.FirstOrDefaultAsync(item => item.Id == decreeId, cancellationToken)
			?? throw new InvalidOperationException($"Decree '{decreeId}' was not found.");

		await markdownStorageService.SaveDecreeAsync(decree, beginBoundary: true, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "decree.begin-boundary", subject: decree, cancellationToken: cancellationToken);
		return decree;
	}

	/// <inheritdoc />
	public async Task DeleteDecreeAsync(string decreeId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(decreeId);
		var decree = await context.Decrees.FirstOrDefaultAsync(item => item.Id == decreeId, cancellationToken)
			?? throw new InvalidOperationException($"Decree '{decreeId}' was not found.");

		var graveyardEntry = await temporalDataService.ArchiveEntityAsync(decree, "api-delete", Environment.UserName, cancellationToken);
		context.Decrees.Remove(decree);
		await context.SaveChangesAsync(cancellationToken);
		await markdownStorageService.DeleteDecreeAsync(decree, cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"decree.delete",
			subjectType: nameof(Decree),
			subjectId: decree.Id,
			subjectTitle: decree.Title,
			temporalKind: "database",
			temporalEntryKey: graveyardEntry.EntryKey,
			temporalEntityType: graveyardEntry.EntityType,
			temporalEntityId: graveyardEntry.EntityId,
			temporalEntityTitle: graveyardEntry.EntityTitle,
			cancellationToken: cancellationToken);
	}

	/// <inheritdoc />
	public async Task<Eventive> MaterializeEventiveAsync(string fateId, EventiveMaterialization request, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fateId);
		ArgumentNullException.ThrowIfNull(request);

		var fate = await context.Fates.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(item => item.Id == fateId, cancellationToken)
			?? throw new InvalidOperationException($"Fate '{fateId}' was not found.");
		if (fate.Status != FateStatus.Active)
		{
			throw new InvalidOperationException($"Fate '{fateId}' is {fate.Status} and does not materialize eventives.");
		}

		var date = request.Date
			?? fate.Date
			?? throw new InvalidOperationException($"Fate '{fateId}' has no occurrence date; supply one to materialize its eventive.");

		// Interaction with an orbit occurrence resolves in preview (non-seeking) mode: a future instance must
		// never advance the schedule state. The occurrence date (and time) is the instance identity, so the
		// later seeking pass recognizes this instance instead of re-creating it. Span-format fate orbits
		// supply the occurrence's own start/end/length, so no duration override is needed.
		Pleiades.Orbits.OrbitOccurrenceInstance? occurrence = null;
		if (!string.IsNullOrWhiteSpace(fate.Orbit))
		{
			var dayOccurrences = await orbitService.PreviewDayOccurrencesAsync(fate, fate.Orbit, date, cancellationToken);
			if (dayOccurrences.Count == 0)
			{
				throw new InvalidOperationException($"Fate '{fateId}' has no orbit occurrence on {date:yyyy-MM-dd}.");
			}

			occurrence = request.StartTime is not null
				? dayOccurrences.FirstOrDefault(item => item.StartTime == request.StartTime) ?? dayOccurrences[0]
				: dayOccurrences[0];
		}

		var startTime = occurrence?.StartTime ?? request.StartTime ?? fate.StartTime;

		// PEP101: a locked whole-fate freezes all materialization; a locked single occurrence blocks just itself.
		if (await dependencyGate.IsFateMaterializationBlockedAsync(fate.Id, date, startTime, cancellationToken))
		{
			throw new InvalidOperationException($"Fate '{fateId}' occurrence on {date:yyyy-MM-dd} is blocked by unmet dependencies and cannot be materialized.");
		}

		var existing = await context.Eventives.FirstOrDefaultAsync(
			item => item.FateId == fate.Id && item.RecurrenceDate == date && item.RecurrenceTime == startTime,
			cancellationToken);
		if (existing is not null)
		{
			return existing;
		}

		var eventive = new Eventive
		{
			FateId = fate.Id,
			Date = date,
			StartTime = startTime,
			EndTime = occurrence?.EndTime ?? request.EndTime ?? fate.EndTime,
			RecurrenceDate = date,
			RecurrenceTime = startTime,
			Estimation = occurrence?.DurationMinutes ?? fate.ResolveEventiveDuration(),
		};
		eventive.Normalize();

		context.Eventives.Add(eventive);
		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"fate.materialize-eventive",
			subjectType: nameof(Eventive),
			subjectId: eventive.Id.ToString(CultureInfo.InvariantCulture),
			details: new { fateId = fate.Id, date = date.ToString("yyyy-MM-dd") },
			cancellationToken: cancellationToken);
		return eventive;
	}

	/// <inheritdoc />
	public async Task<Attentive> MaterializeAttentiveAsync(string decreeId, AttentiveMaterialization request, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(decreeId);
		ArgumentNullException.ThrowIfNull(request);

		var decree = await context.Decrees.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(item => item.Id == decreeId, cancellationToken)
			?? throw new InvalidOperationException($"Decree '{decreeId}' was not found.");
		if (decree.Status != DecreeStatus.Active)
		{
			throw new InvalidOperationException($"Decree '{decreeId}' is {decree.Status} and does not materialize attentives.");
		}

		var date = request.Date ?? DateOnly.FromDateTime(DateTime.Today);

		// Interaction with an orbit occurrence resolves in preview (non-seeking) mode; see the fate flow.
		// Decrees resolve on the Pleiadean calendar. Without an orbit, an unbound attentive may sit at any
		// time and date the caller chooses.
		Pleiades.Orbits.OrbitOccurrenceInstance? occurrence = null;
		if (!string.IsNullOrWhiteSpace(decree.Orbit))
		{
			var dayOccurrences = await orbitService.PreviewDayOccurrencesAsync(decree, decree.Orbit, date, cancellationToken);
			if (dayOccurrences.Count == 0)
			{
				throw new InvalidOperationException($"Decree '{decreeId}' has no orbit occurrence on {date:yyyy-MM-dd}.");
			}

			occurrence = request.Time is not null
				? dayOccurrences.FirstOrDefault(item => item.StartTime == request.Time) ?? dayOccurrences[0]
				: dayOccurrences[0];
		}

		var occurrenceDate = occurrence?.Date ?? date;
		var occurrenceTime = occurrence?.StartTime ?? request.Time;
		var existing = await context.Attentives.FirstOrDefaultAsync(
			item => item.DecreeId == decree.Id && item.Date == occurrenceDate && item.Time == occurrenceTime && item.PolarisCycleId == null,
			cancellationToken);
		if (existing is not null)
		{
			return existing;
		}

		var attentive = new Attentive
		{
			DecreeId = decree.Id,
			Date = occurrenceDate,
			Time = occurrenceTime,
			PeriodEndDate = occurrence is not null && occurrence.PeriodEndExclusive > occurrence.Date.AddDays(1)
				? occurrence.PeriodEndExclusive
				: null,
			Estimation = request.Estimation ?? decree.DefaultLength,
			Minimum = request.Minimum,
			Maximum = request.Maximum,
		};
		attentive.Normalize();

		context.Attentives.Add(attentive);
		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"decree.materialize-attentive",
			subjectType: nameof(Attentive),
			subjectId: attentive.Id.ToString(CultureInfo.InvariantCulture),
			details: new { decreeId = decree.Id, date = date.ToString("yyyy-MM-dd") },
			cancellationToken: cancellationToken);
		return attentive;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Eventive>> ListEventivesAsync(string? fateId = null, string? objectiveId = null, CancellationToken cancellationToken = default)
	{
		var query = context.Eventives.AsNoTracking();
		if (!string.IsNullOrWhiteSpace(fateId))
		{
			query = query.Where(item => item.FateId == fateId);
		}

		if (!string.IsNullOrWhiteSpace(objectiveId))
		{
			query = query.Where(item => item.ObjectiveId == objectiveId);
		}

		return await query
			.OrderBy(item => item.Date)
			.ThenBy(item => item.StartTime)
			.ToListAsync(cancellationToken);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Attentive>> ListAttentivesAsync(string? decreeId = null, CancellationToken cancellationToken = default)
	{
		var query = context.Attentives.AsNoTracking();
		if (!string.IsNullOrWhiteSpace(decreeId))
		{
			query = query.Where(item => item.DecreeId == decreeId);
		}

		return await query
			.OrderBy(item => item.Date)
			.ThenBy(item => item.Time)
			.ToListAsync(cancellationToken);
	}

	/// <inheritdoc />
	public async Task<Eventive> UpdateEventiveAsync(long eventiveId, EventiveUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(update);
		var eventive = await context.Eventives.FirstOrDefaultAsync(item => item.Id == eventiveId, cancellationToken)
			?? throw new InvalidOperationException($"Eventive '{eventiveId}' was not found.");

		// Eventives are never Polaris-bound, so moving their time specification is always allowed.
		if (update.Date is not null)
		{
			eventive.Date = update.Date.Value;
		}

		if (update.StartTime is not null)
		{
			eventive.StartTime = update.StartTime;
		}

		if (update.EndTime is not null)
		{
			eventive.EndTime = update.EndTime;
		}

		if (update.Resolution is not null)
		{
			eventive.Resolution = update.Resolution.Value;
		}

		ApplyAllocations(eventive, update.Estimation, update.Minimum, update.Maximum);
		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"eventive.update",
			subjectType: nameof(Eventive),
			subjectId: eventive.Id.ToString(CultureInfo.InvariantCulture),
			details: new { eventive.FateId, eventive.ObjectiveId, resolution = eventive.Resolution.ToString(), date = eventive.Date.ToString("yyyy-MM-dd") },
			cancellationToken: cancellationToken);
		return eventive;
	}

	/// <inheritdoc />
	public async Task<Attentive> UpdateAttentiveAsync(long attentiveId, AttentiveUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(update);
		var attentive = await context.Attentives.FirstOrDefaultAsync(item => item.Id == attentiveId, cancellationToken)
			?? throw new InvalidOperationException($"Attentive '{attentiveId}' was not found.");

		if (update.Date is not null)
		{
			if (attentive.IsBound)
			{
				throw new InvalidOperationException("A Polaris-bound attentive cannot be rescheduled; it can only be done, skipped, or moved to another Polaris cycle.");
			}

			attentive.Date = update.Date.Value;
		}

		if (!string.IsNullOrWhiteSpace(update.MoveToPolarisCycleId))
		{
			if (!attentive.IsBound)
			{
				throw new InvalidOperationException("An unbound attentive is not part of a Polaris cycle and cannot be moved between cycles; reschedule it instead.");
			}

			var targetExists = await context.PolarisCycles.AnyAsync(item => item.Id == update.MoveToPolarisCycleId, cancellationToken);
			if (!targetExists)
			{
				throw new InvalidOperationException($"Polaris cycle '{update.MoveToPolarisCycleId}' was not found.");
			}

			attentive.PolarisCycleId = update.MoveToPolarisCycleId;
		}

		if (update.Time is not null)
		{
			attentive.Time = update.Time;
		}

		if (update.Resolution is not null)
		{
			attentive.Resolution = update.Resolution.Value;
		}

		ApplyAllocations(attentive, update.Estimation, update.Minimum, update.Maximum);
		await context.SaveChangesAsync(cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"attentive.update",
			subjectType: nameof(Attentive),
			subjectId: attentive.Id.ToString(CultureInfo.InvariantCulture),
			details: new { attentive.DecreeId, attentive.PolarisCycleId, resolution = attentive.Resolution.ToString(), date = attentive.Date.ToString("yyyy-MM-dd") },
			cancellationToken: cancellationToken);
		return attentive;
	}

	private static void ApplyAllocations(ITimeAllocated record, int? estimation, int? minimum, int? maximum)
	{
		if (estimation is not null)
		{
			record.Estimation = estimation;
		}

		if (minimum is not null)
		{
			record.Minimum = minimum;
		}

		if (maximum is not null)
		{
			record.Maximum = maximum;
		}

		record.Normalize();
	}

	private async Task ApplyParentAsync(Incentive child, string parentIncentiveId, CancellationToken cancellationToken)
	{
		var parent = await context.Incentives
			.AsNoTracking()
			.IgnoreAutoIncludes()
			.FirstOrDefaultAsync(item => item.Id == parentIncentiveId, cancellationToken)
			?? throw new InvalidOperationException($"Parent incentive '{parentIncentiveId}' was not found.");

		IncentiveParenting.EnsureValidParent(child, parent);
		child.ParentIncentiveId = parent.Id;
	}

	private async Task EnsureDirectiveExistsAsync(string? directiveId, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(directiveId))
		{
			return;
		}

		var exists = await context.Directives.AnyAsync(item => item.Id == directiveId, cancellationToken);
		if (!exists)
		{
			throw new InvalidOperationException($"Directive '{directiveId}' was not found.");
		}
	}

	private static Fate CloneFate(Fate fate)
	{
		return new Fate
		{
			Id = fate.Id,
			Title = fate.Title,
			DirectiveId = fate.DirectiveId,
			ParentIncentiveId = fate.ParentIncentiveId,
			Status = fate.Status,
			Orbit = fate.Orbit,
			Date = fate.Date,
			StartTime = fate.StartTime,
			EndTime = fate.EndTime,
			EventDuration = fate.EventDuration,
		};
	}

	private static Decree CloneDecree(Decree decree)
	{
		return new Decree
		{
			Id = decree.Id,
			Title = decree.Title,
			DirectiveId = decree.DirectiveId,
			Status = decree.Status,
			Orbit = decree.Orbit,
			DefaultLength = decree.DefaultLength,
			ActiveCelestron = decree.ActiveCelestron,
			Reflect = decree.Reflect,
		};
	}

	private static void ValidateEventWindow(TimeOnly? startTime, TimeOnly? endTime)
	{
		if (startTime is null && endTime is not null)
		{
			throw new ArgumentException("A fate cannot carry an end time without a start time.");
		}

		if (startTime is not null && endTime is not null && endTime < startTime)
		{
			throw new ArgumentException("Fate end time cannot be earlier than its start time.");
		}
	}
}
