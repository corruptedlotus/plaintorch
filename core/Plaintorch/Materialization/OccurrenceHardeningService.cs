using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Pleiades.Orbits;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.State;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Materialization;

/// <summary>
/// The single choke point that hardens a declarative occurrence into a persisted row (Strategy 1 / soft
/// agenda, PEP100/PEP101). Every interaction with — and every reference to — an occurrence routes through
/// here, so a projected agenda item becomes a durable <see cref="Eventive"/>/<see cref="Attentive"/> the
/// moment it is acted on.
/// </summary>
/// <remarks>
/// Orbit occurrences resolve in PREVIEW (non-seeking) mode: interacting with a future occurrence must never
/// advance the schedule cursor. The occurrence's RECURRENCE-ID (owner id + date + time) is its identity, so
/// the seeking/harden pass recognizes an already-hardened occurrence instead of duplicating it, and a
/// re-harden is idempotent. The interactive entry points save; <see cref="EnsureReferencedEventiveAsync"/>
/// hardens into the current unit of work without saving, for the state-policy enforcement rule.
/// </remarks>
public sealed class OccurrenceHardeningService(
	PlainfraContext context,
	PlaintorchOrbitService orbitService,
	DependencyGateService dependencyGate,
	VaultAuditLogService auditLogService)
{
	/// <summary>
	/// Hardens a fate's eventive for an occurrence, returning the existing row when already hardened.
	/// </summary>
	public async Task<Eventive> HardenFateOccurrenceAsync(string fateId, EventiveMaterialization request, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fateId);
		ArgumentNullException.ThrowIfNull(request);

		var fate = await context.Fates.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(item => item.Id == fateId, cancellationToken)
			?? throw new InvalidOperationException($"Fate '{fateId}' was not found.");

		var (eventive, created) = await EnsureFateEventiveIntoContextAsync(fate, request, respectDependencyGate: true, cancellationToken);
		if (created)
		{
			await context.SaveChangesAsync(cancellationToken);
			await auditLogService.WriteAsync(
				"api",
				"fate.materialize-eventive",
				subjectType: nameof(Eventive),
				subjectId: eventive.Id.ToString(CultureInfo.InvariantCulture),
				details: new { fateId = fate.Id, date = eventive.RecurrenceDate.ToString("yyyy-MM-dd") },
				cancellationToken: cancellationToken);
		}

		return eventive;
	}

	/// <summary>
	/// Hardens a decree's unbound attentive for an occurrence, returning the existing row when already hardened.
	/// </summary>
	public async Task<Attentive> HardenDecreeOccurrenceAsync(string decreeId, AttentiveMaterialization request, CancellationToken cancellationToken = default)
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
		OrbitOccurrenceInstance? occurrence = null;
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
			item => item.DecreeId == decree.Id && item.RecurrenceDate == occurrenceDate && item.RecurrenceTime == occurrenceTime && item.PolarisCycleId == null,
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
			RecurrenceDate = occurrenceDate,
			RecurrenceTime = occurrenceTime,
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

	/// <summary>
	/// Hardens an objective's due-date eventive, returning the existing row when already hardened.
	/// </summary>
	public async Task<Eventive> HardenObjectiveOccurrenceAsync(string objectiveId, EventiveMaterialization request, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(objectiveId);
		ArgumentNullException.ThrowIfNull(request);

		var objective = await context.Objectives
			.AsNoTracking()
			.IgnoreAutoIncludes()
			.FirstOrDefaultAsync(item => item.Id == objectiveId, cancellationToken)
			?? throw new InvalidOperationException($"Objective '{objectiveId}' was not found.");

		var (eventive, created) = await EnsureObjectiveEventiveIntoContextAsync(objective, request, cancellationToken);
		if (created)
		{
			await context.SaveChangesAsync(cancellationToken);
			await auditLogService.WriteAsync(
				"api",
				"objective.materialize-eventive",
				subjectType: nameof(Eventive),
				subjectId: eventive.Id.ToString(CultureInfo.InvariantCulture),
				details: new { objectiveId = objective.Id, date = eventive.RecurrenceDate.ToString("yyyy-MM-dd") },
				cancellationToken: cancellationToken);
		}

		return eventive;
	}

	/// <summary>
	/// Hardens the eventive an owner (fate or objective) owns for an occurrence, auto-detecting the owner kind.
	/// The entry point for interacting with a projected eventive by its recurrence-id.
	/// </summary>
	public async Task<Eventive> HardenEventiveOccurrenceAsync(string ownerId, EventiveMaterialization request, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
		if (await context.Fates.AsNoTracking().AnyAsync(item => item.Id == ownerId, cancellationToken))
		{
			return await HardenFateOccurrenceAsync(ownerId, request, cancellationToken);
		}

		if (await context.Objectives.AsNoTracking().AnyAsync(item => item.Id == ownerId, cancellationToken))
		{
			return await HardenObjectiveOccurrenceAsync(ownerId, request, cancellationToken);
		}

		throw new InvalidOperationException($"No fate or objective '{ownerId}' owns an eventive.");
	}

	/// <summary>
	/// Ensures the eventive that a dependency endpoint references is hardened into the current unit of work
	/// (no save — the ongoing <see cref="PlainfraContext.SaveChanges()"/> persists it), so a reference to a
	/// projected occurrence makes it a durable row the reconciler can resolve. No-op for non-eventive endpoints
	/// and already-hardened occurrences; an occurrence that cannot be hardened (locked, or off-phase for its
	/// orbit) is left unresolved rather than failing the save.
	/// </summary>
	public async Task EnsureReferencedEventiveAsync(EndpointRef endpoint, CancellationToken cancellationToken = default)
	{
		if (endpoint.Kind != DependencyEndpointKind.Eventive || endpoint.RecurrenceDate is not DateOnly date)
		{
			return;
		}

		if (await FindEventiveAsync(endpoint.Id, date, endpoint.RecurrenceTime, cancellationToken) is not null)
		{
			return;
		}

		var request = new EventiveMaterialization(Date: date, StartTime: endpoint.RecurrenceTime);

		var fate = await context.Fates.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(item => item.Id == endpoint.Id, cancellationToken);
		if (fate is not null)
		{
			try
			{
				// A reference hardens the occurrence even when it is dependency-gated: the row must exist for the
				// reference to resolve; the lock still applies to whether it can begin.
				await EnsureFateEventiveIntoContextAsync(fate, request, respectDependencyGate: false, cancellationToken);
			}
			catch (InvalidOperationException)
			{
			}

			return;
		}

		var objective = await context.Objectives.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(item => item.Id == endpoint.Id, cancellationToken);
		if (objective is not null)
		{
			try
			{
				await EnsureObjectiveEventiveIntoContextAsync(objective, request, cancellationToken);
			}
			catch (InvalidOperationException)
			{
			}
		}
	}

	private async Task<(Eventive Eventive, bool Created)> EnsureFateEventiveIntoContextAsync(Fate fate, EventiveMaterialization request, bool respectDependencyGate, CancellationToken cancellationToken)
	{
		if (fate.Status != FateStatus.Active)
		{
			throw new InvalidOperationException($"Fate '{fate.Id}' is {fate.Status} and does not materialize eventives.");
		}

		var date = request.Date
			?? fate.Date
			?? throw new InvalidOperationException($"Fate '{fate.Id}' has no occurrence date; supply one to materialize its eventive.");

		OrbitOccurrenceInstance? occurrence = null;
		if (!string.IsNullOrWhiteSpace(fate.Orbit))
		{
			var dayOccurrences = await orbitService.PreviewDayOccurrencesAsync(fate, fate.Orbit, date, cancellationToken);
			if (dayOccurrences.Count == 0)
			{
				throw new InvalidOperationException($"Fate '{fate.Id}' has no orbit occurrence on {date:yyyy-MM-dd}.");
			}

			occurrence = request.StartTime is not null
				? dayOccurrences.FirstOrDefault(item => item.StartTime == request.StartTime) ?? dayOccurrences[0]
				: dayOccurrences[0];
		}

		var startTime = occurrence?.StartTime ?? request.StartTime ?? fate.StartTime;

		// PEP101: a locked whole-fate freezes all materialization; a locked single occurrence blocks just itself.
		if (respectDependencyGate && await dependencyGate.IsFateMaterializationBlockedAsync(fate.Id, date, startTime, cancellationToken))
		{
			throw new InvalidOperationException($"Fate '{fate.Id}' occurrence on {date:yyyy-MM-dd} is blocked by unmet dependencies and cannot be materialized.");
		}

		var existing = await FindEventiveAsync(fate.Id, date, startTime, cancellationToken);
		if (existing is not null)
		{
			return (existing, false);
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
		return (eventive, true);
	}

	private async Task<(Eventive Eventive, bool Created)> EnsureObjectiveEventiveIntoContextAsync(Objective objective, EventiveMaterialization request, CancellationToken cancellationToken)
	{
		var date = request.Date
			?? objective.Due
			?? throw new InvalidOperationException($"Objective '{objective.Id}' has no due date; supply a date to materialize its eventive.");

		var existing = await FindEventiveAsync(objective.Id, date, null, cancellationToken);
		if (existing is not null)
		{
			return (existing, false);
		}

		var eventive = new Eventive
		{
			ObjectiveId = objective.Id,
			Date = date,
			StartTime = request.StartTime,
			EndTime = request.EndTime,
			RecurrenceDate = date,
			RecurrenceTime = request.StartTime,
		};
		eventive.Normalize();
		context.Eventives.Add(eventive);
		return (eventive, true);
	}

	/// <summary>
	/// Finds an eventive for an owner UID and RECURRENCE-ID, seeing both a row already added in the current unit
	/// of work (so two references in one save do not duplicate it) and the database.
	/// </summary>
	private async Task<Eventive?> FindEventiveAsync(string ownerUid, DateOnly date, TimeOnly? time, CancellationToken cancellationToken)
	{
		var tracked = context.ChangeTracker.Entries<Eventive>()
			.FirstOrDefault(entry => entry.State == EntityState.Added
				&& (entry.Entity.FateId == ownerUid || entry.Entity.ObjectiveId == ownerUid)
				&& entry.Entity.RecurrenceDate == date
				&& entry.Entity.RecurrenceTime == time)?.Entity;
		if (tracked is not null)
		{
			return tracked;
		}

		return await context.Eventives.FirstOrDefaultAsync(item =>
			(item.FateId == ownerUid || item.ObjectiveId == ownerUid)
			&& item.RecurrenceDate == date
			&& item.RecurrenceTime == time,
			cancellationToken);
	}
}
