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
/// Declaratives are event-like: they are never acted on directly. Interacting with a single occurrence
/// addresses it by its RECURRENCE-ID (an eventive for fates/objectives, an unbound attentive for decrees);
/// the occurrence is resolved into the same unit of work and the interaction applied, so a projected occurrence
/// hardens as the guaranteed consequence of the single save rather than through a separate materialize verb.
/// Instances resolved here are never Polaris-bound; binding only happens by manually adding a decree to a
/// Polaris cycle through the Polaris API, and a bound attentive — having no meaningful recurrence-id — is
/// addressed by its row id instead. Interaction with an orbit-scheduled occurrence is resolved through preview
/// (non-seeking) resolution so a future instance never pushes the schedule state forward; its recurrence-id is
/// its identity, so the later seeking pass recognizes it.
/// </remarks>
public sealed class DeclarativeApiService(
	PlainfraContext context,
	PuckCreationService puckCreationService,
	PlaintorchMarkdownStorageService markdownStorageService,
	VaultTemporalDataService temporalDataService,
	DependencyGateService dependencyGate,
	OccurrenceHardeningService hardeningService,
	VaultEntityLifecycleService lifecycleService,
	VaultAuditLogService auditLogService) : IDeclarativeApi
{
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

		// Orbit and a fixed date are mutually exclusive (PEP100); a recurring plan keeps only the orbit, dropping any
		// stray one-off date/time so the fate is never born carrying both shapes.
		var recurring = !string.IsNullOrWhiteSpace(plan.Orbit);
		var fate = new Fate
		{
			Id = puckCreationService.CreateIdFor<Fate>(plan.Id),
			Title = plan.Title,
			DirectiveId = plan.DirectiveId,
			Date = recurring ? null : plan.Date,
			StartTime = recurring ? null : plan.StartTime,
			EndTime = recurring ? null : plan.EndTime,
			Orbit = plan.Orbit,
			EventDuration = plan.EventDuration,
		};

		if (!string.IsNullOrWhiteSpace(plan.ParentIncentiveId))
		{
			await ApplyParentAsync(fate, plan.ParentIncentiveId, cancellationToken);
		}

		context.Fates.Add(fate);
		await context.SaveChangesAsync(cancellationToken);
		await markdownStorageService.SaveFateAsync(fate, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "fate.create", subject: fate, cancellationToken: cancellationToken);
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

		if (update.ParentIncentiveId.IsSet)
		{
			if (string.IsNullOrWhiteSpace(update.ParentIncentiveId.Value))
			{
				fate.ParentIncentiveId = null;
			}
			else
			{
				await ApplyParentAsync(fate, update.ParentIncentiveId.Value, cancellationToken);
			}
		}

		if (update.Date.IsSet)
		{
			// A null date drops the fixed date — e.g. switching a one-off fate onto a recurring orbit, so it no
			// longer materializes a standalone eventive alongside the orbit's occurrences.
			fate.Date = update.Date.Value;
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

		// Orbit and a fixed date are mutually exclusive (PEP100). Whichever this update sets clears the other, so a
		// caller never has to send an explicit clear alongside — the schedule interception keeps the fate single-shaped.
		var setsOrbit = update.Orbit is not null && !string.IsNullOrWhiteSpace(update.Orbit);
		var setsDate = update.Date.IsSet && update.Date.Value is not null;
		if (setsOrbit)
		{
			fate.Date = null;
			fate.StartTime = null;
			fate.EndTime = null;
		}
		else if (setsDate)
		{
			fate.Orbit = null;
		}

		ValidateEventWindow(fate.StartTime, fate.EndTime);

		await context.SaveChangesAsync(cancellationToken);
		await markdownStorageService.SaveFateAsync(fate, previous, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "fate.update", subject: fate, cancellationToken: cancellationToken);
		return fate;
	}

	/// <inheritdoc />
	public async Task<Fate> BeginFateBoundaryAsync(string fateId, CancellationToken cancellationToken = default)
		=> (Fate)await lifecycleService.BeginBoundaryAsync(typeof(Fate), fateId, cancellationToken);

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
		await context.SaveChangesAsync(cancellationToken);
		await markdownStorageService.SaveDecreeAsync(decree, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "decree.create", subject: decree, cancellationToken: cancellationToken);
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

		await context.SaveChangesAsync(cancellationToken);
		await markdownStorageService.SaveDecreeAsync(decree, previous, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "decree.update", subject: decree, cancellationToken: cancellationToken);
		return decree;
	}

	/// <inheritdoc />
	public async Task<Decree> BeginDecreeBoundaryAsync(string decreeId, CancellationToken cancellationToken = default)
		=> (Decree)await lifecycleService.BeginBoundaryAsync(typeof(Decree), decreeId, cancellationToken);

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
	public async Task<Eventive> UpdateEventiveAsync(EventiveOccurrenceRef occurrence, EventiveUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(occurrence);
		ArgumentNullException.ThrowIfNull(update);

		// Resolve the occurrence into the current unit of work (evaluate the orbit, find its hardened twin, or
		// build the projected row) without a save of its own; the single SaveChanges below persists the
		// materialization together with this interaction, and the state-policy pass runs over it centrally. The
		// recurrence-id resolves a projected occurrence and its hardened twin identically, so no row id is needed.
		var eventive = await hardeningService.EnsureEventiveIntoContextAsync(
			occurrence.OwnerId,
			new EventiveMaterialization(Date: occurrence.RecurrenceDate, StartTime: occurrence.RecurrenceTime),
			cancellationToken);

		// Eventives are never Polaris-bound, so moving their time specification is always allowed.
		if (update.Date is not null)
		{
			eventive.Date = update.Date.Value;
		}

		if (update.StartTime.IsSet)
		{
			eventive.StartTime = update.StartTime.Value;
		}

		if (update.EndTime.IsSet)
		{
			eventive.EndTime = update.EndTime.Value;
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
	public async Task<Attentive> UpdateAttentiveAsync(AttentiveOccurrenceRef occurrence, AttentiveUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(occurrence);
		ArgumentNullException.ThrowIfNull(update);

		Attentive attentive;
		if (occurrence.Id is long id)
		{
			// By row id: a Polaris-bound occurrence (placed into a cycle by hand) has no meaningful recurrence-id, so
			// it is addressed directly. The row already exists, so no materialization is involved.
			attentive = await context.Attentives.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
				?? throw new InvalidOperationException($"Attentive '{id}' was not found.");
		}
		else
		{
			if (string.IsNullOrWhiteSpace(occurrence.DecreeId) || occurrence.RecurrenceDate is not DateOnly recurrenceDate)
			{
				throw new ArgumentException("An attentive occurrence must be addressed by row id, or by decree and recurrence date.", nameof(occurrence));
			}

			// By recurrence-id: an unbound occurrence, possibly still a projection. Resolve it into the current unit
			// of work without a save of its own; the single SaveChanges below persists the materialization together
			// with this interaction, and the state-policy pass runs over it centrally.
			attentive = await hardeningService.EnsureDecreeAttentiveIntoContextAsync(
				occurrence.DecreeId,
				new AttentiveMaterialization(Date: recurrenceDate, Time: occurrence.RecurrenceTime),
				cancellationToken);
		}

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

		if (update.Time.IsSet)
		{
			attentive.Time = update.Time.Value;
		}

		if (update.Resolution is not null)
		{
			attentive.Resolution = update.Resolution.Value;
		}

		ApplyAllocations(attentive, update.Estimation, update.Minimum, update.Maximum);

		if (update.AffinityTimeframeId.IsSet)
		{
			if (update.AffinityTimeframeId.Value is long timeframeId
				&& !await context.Timeframes.AnyAsync(item => item.Id == timeframeId, cancellationToken))
			{
				throw new InvalidOperationException($"Timeframe '{timeframeId}' was not found.");
			}

			attentive.AffinityTimeframeId = update.AffinityTimeframeId.Value;
		}

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

	private static void ApplyAllocations(ITimeAllocated record, Optional<int?> estimation, Optional<int?> minimum, Optional<int?> maximum)
	{
		// A set field applies its value — including null, which clears the allocation; an unset field is left alone.
		if (estimation.IsSet)
		{
			record.Estimation = estimation.Value;
		}

		if (minimum.IsSet)
		{
			record.Minimum = minimum.Value;
		}

		if (maximum.IsSet)
		{
			record.Maximum = maximum.Value;
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
