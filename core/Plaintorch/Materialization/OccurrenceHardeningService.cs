using Microsoft.EntityFrameworkCore;
using Pleiades.Orbits;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.State;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Materialization;

/// <summary>
/// The centralized choke point that resolves a declarative occurrence into the current unit of work as a
/// hardened row (Strategy 1 / soft agenda, PEP100/PEP101). Every interaction with — and every reference to —
/// an occurrence routes through here, so a projected agenda item becomes a durable <see cref="Eventive"/>/
/// <see cref="Attentive"/> the moment it is acted on.
/// </summary>
/// <remarks>
/// Every entry point is SAVE-FREE: it finds the already-hardened row or builds a new one and tracks it into the
/// caller's <see cref="PlainfraContext"/>, and the single <see cref="PlainfraContext.SaveChanges()"/> is left to
/// the caller — an interaction endpoint, or the state-policy save pass that guarantees a referenced occurrence
/// hardens. Materialization is thus never a standalone verb; it is the guaranteed consequence of touching an
/// occurrence, enforced centrally on the save.
/// <para>
/// Orbit occurrences resolve in PREVIEW (non-seeking) mode: interacting with a future occurrence must never
/// advance the schedule cursor. The occurrence's RECURRENCE-ID (owner id + date + time) is its identity, so an
/// already-hardened occurrence — including one that was rescheduled — is recognized rather than duplicated, and
/// a re-resolve is idempotent.
/// </para>
/// </remarks>
public sealed class OccurrenceHardeningService(
	PlainfraContext context,
	PlaintorchOrbitService orbitService,
	DependencyGateService dependencyGate)
{
	/// <summary>
	/// Resolves the eventive an owner (a fate or an objective, auto-detected) owns for an occurrence into the
	/// current unit of work, returning the existing row when already hardened. The dependency gate is respected:
	/// a locked occurrence refuses to harden (PEP101). No save — the caller's <see cref="PlainfraContext.SaveChanges()"/>
	/// persists it. The entry point for interacting with a projected eventive by its recurrence-id.
	/// </summary>
	public async Task<Eventive> EnsureEventiveIntoContextAsync(string ownerId, EventiveMaterialization request, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
		ArgumentNullException.ThrowIfNull(request);

		var fate = await context.Fates.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(item => item.Id == ownerId, cancellationToken);
		if (fate is not null)
		{
			var (eventive, _) = await EnsureFateEventiveIntoContextAsync(fate, request, respectDependencyGate: true, cancellationToken);
			return eventive;
		}

		var objective = await context.Objectives.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(item => item.Id == ownerId, cancellationToken);
		if (objective is not null)
		{
			var (eventive, _) = await EnsureObjectiveEventiveIntoContextAsync(objective, request, cancellationToken);
			return eventive;
		}

		throw new InvalidOperationException($"No fate or objective '{ownerId}' owns an eventive.");
	}

	/// <summary>
	/// Resolves a decree's unbound attentive for an occurrence into the current unit of work, returning the
	/// existing row when already hardened. No save — the caller's <see cref="PlainfraContext.SaveChanges()"/>
	/// persists it. The entry point for interacting with a projected unbound attentive by its recurrence-id.
	/// </summary>
	public async Task<Attentive> EnsureDecreeAttentiveIntoContextAsync(string decreeId, AttentiveMaterialization request, CancellationToken cancellationToken = default)
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

		// Interaction with an orbit occurrence resolves in preview (non-seeking) mode. Decrees resolve on the
		// Pleiadean calendar. Without an orbit, an unbound attentive may sit at any time and date the caller chooses.
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
			item => item.DecreeId == decree.Id && item.RecurrenceDate == occurrenceDate && item.RecurrenceTime == occurrenceTime,
			cancellationToken);
		if (existing is not null)
		{
			return existing;
		}

		var attentive = new Attentive
		{
			DecreeId = decree.Id,
			Epoch = Epoch.From(occurrenceDate, occurrenceTime, occurrence?.Granularity ?? (occurrenceTime is null ? OrbitUnit.Day : OrbitUnit.Minute)),
			RecurrenceDate = occurrenceDate,
			RecurrenceTime = occurrenceTime,
			PeriodEndDate = occurrence is not null && occurrence.PeriodEndExclusive > occurrence.Date.AddDays(1)
				? occurrence.PeriodEndExclusive
				: null,
		};
		context.Attentives.Add(attentive);
		return attentive;
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
		// A cancelled fate generates nothing and cannot be interacted with. An opted-out fate still honours the
		// user-interaction rule (PEP100/PEP111): interacting with one of its occurrences hardens it, stamped OptOut
		// so it stays hidden until the row is explicitly opted back in by setting it Pending.
		if (fate.Status == FateStatus.Cancelled)
		{
			throw new InvalidOperationException($"Fate '{fate.Id}' is {fate.Status} and does not materialize eventives.");
		}

		var date = request.Date
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

		var startTime = occurrence?.StartTime ?? request.StartTime;

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

		var endTime = occurrence?.EndTime ?? request.EndTime;
		// Temporal span (Epoch.Duration): the orbit span or explicit start/end window, null for an all-day occurrence.
		var spanMinutes = occurrence?.DurationMinutes ?? OccurrenceDurations.SpanMinutes(startTime, endTime);
		var granularity = occurrence?.Granularity ?? (startTime is null ? OrbitUnit.Day : OrbitUnit.Minute);

		var eventive = new Eventive
		{
			FateId = fate.Id,
			Epoch = Epoch.From(date, startTime, granularity, spanMinutes),
			RecurrenceDate = date,
			RecurrenceTime = startTime,
			Resolution = fate.Status == FateStatus.OptOut ? EventiveResolution.OptOut : EventiveResolution.Pending,
		};
		context.Eventives.Add(eventive);
		return (eventive, true);
	}

	private async Task<(Eventive Eventive, bool Created)> EnsureObjectiveEventiveIntoContextAsync(Objective objective, EventiveMaterialization request, CancellationToken cancellationToken)
	{
		var date = request.Date
			?? objective.Due?.Date
			?? throw new InvalidOperationException($"Objective '{objective.Id}' has no due date; supply a date to materialize its eventive.");

		var existing = await FindEventiveAsync(objective.Id, date, null, cancellationToken);
		if (existing is not null)
		{
			return (existing, false);
		}

		var eventive = new Eventive
		{
			ObjectiveId = objective.Id,
			Epoch = Epoch.From(date, request.StartTime, request.StartTime is null ? OrbitUnit.Day : OrbitUnit.Minute, OccurrenceDurations.SpanMinutes(request.StartTime, request.EndTime)),
			RecurrenceDate = date,
			RecurrenceTime = request.StartTime,
		};
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
