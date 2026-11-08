using Microsoft.EntityFrameworkCore;
using Pleiades.Orbits;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Plaintorch.State;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Materialization;

/// <summary>
/// The centralized choke point that resolves a declarative occurrence into the current unit of work as a
/// hardened row (Strategy 1 / soft agenda, PEP100/PEP101). Every interaction with — and every reference to —
/// an occurrence routes through here, so a projected agenda item becomes a durable <see cref="Eventive"/>,
/// <see cref="Attentive"/> or <see cref="Terminative"/> the moment it is acted on.
/// </summary>
/// <remarks>
/// Every entry point is SAVE-FREE: it finds the already-hardened row or builds a new one and tracks it into the
/// caller's <see cref="PlainfraContext"/>, and the single <see cref="PlainfraContext.SaveChanges()"/> is left to
/// the caller — an interaction endpoint, or the state engine's save pass that guarantees a referenced occurrence
/// hardens. Materialization is thus never a standalone verb; it is the guaranteed consequence of touching an
/// occurrence, enforced centrally on the save.
/// <para>
/// Orbit occurrences resolve in PREVIEW (non-seeking) mode: interacting with a future occurrence must never
/// advance the schedule cursor. The occurrence's RECURRENCE-ID (owner id + original slot moment) is its identity,
/// so an already-hardened occurrence — including one that was rescheduled — is recognized rather than duplicated,
/// and a re-resolve is idempotent.
/// </para>
/// </remarks>
public sealed class OccurrenceHardeningService(
	PlainfraContext context,
	PlaintorchOrbitService orbitService,
	DependencyGateService dependencyGate,
	TerminativeService terminativeService)
{
	/// <summary>
	/// Resolves the eventive a fate owns for an occurrence into the current unit of work, returning the existing row when
	/// already hardened. The dependency gate is respected: a locked occurrence refuses to harden (PEP101). No save — the
	/// caller's <see cref="PlainfraContext.SaveChanges()"/> persists it. The entry point for interacting with a projected
	/// eventive by its recurrence-id.
	/// </summary>
	public async Task<Eventive> EnsureEventiveIntoContextAsync(string ownerId, DateTime recurrenceId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

		var fate = await context.Fates.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(item => item.Id == ownerId, cancellationToken)
			?? throw new InvalidOperationException($"No fate '{ownerId}' owns an eventive.");
		return await EnsureFateEventiveIntoContextAsync(fate, recurrenceId, cancellationToken);
	}

	/// <summary>
	/// Resolves the terminative an item's due spawns (PEP103 patch 7) into the current unit of work, returning the existing
	/// row when already hardened, whatever its origin's state now. A new one hardens only while the origin's due acts: an
	/// item with no due, or finished or set aside, projects none to harden. No save — the caller's
	/// <see cref="PlainfraContext.SaveChanges()"/> persists it, and the state engine sets its resolution. The entry point for
	/// interacting with a projected terminative by its origin.
	/// </summary>
	public async Task<Terminative> EnsureTerminativeIntoContextAsync(string originId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(originId);

		var origin = await terminativeService.FindOriginAsync(context, originId, cancellationToken)
			?? throw new InvalidOperationException($"No objective, stellar directive or checkpoint '{originId}' carries a due.");
		if ((await terminativeService.FindOfAsync(context, [origin], cancellationToken)).FirstOrDefault() is { } existing)
		{
			return existing;
		}

		if (origin.Due is null)
		{
			throw new InvalidOperationException($"{origin.GetType().Name} '{origin.Id}' has no due, so it has no terminative.");
		}

		if (!terminativeService.Acts(origin))
		{
			throw new InvalidOperationException($"{origin.GetType().Name} '{origin.Id}' is finished or set aside, and its due no longer spawns a terminative.");
		}

		var terminative = terminativeService.Spawn(origin);
		context.Terminatives.Add(terminative);
		return terminative;
	}

	/// <summary>
	/// Resolves a decree's unbound attentive for an occurrence into the current unit of work, returning the
	/// existing row when already hardened. No save — the caller's <see cref="PlainfraContext.SaveChanges()"/>
	/// persists it. The entry point for interacting with a projected unbound attentive by its recurrence-id.
	/// </summary>
	public async Task<Attentive> EnsureDecreeAttentiveIntoContextAsync(string decreeId, DateTime recurrenceId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(decreeId);

		var decree = await context.Decrees.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(item => item.Id == decreeId, cancellationToken)
			?? throw new InvalidOperationException($"Decree '{decreeId}' was not found.");
		if (!TemporalActivation.IsActive(decree.Status))
		{
			throw new InvalidOperationException($"Decree '{decreeId}' is {decree.Status} and does not materialize attentives.");
		}

		// Decrees resolve on the Pleiadean calendar. Without an orbit, an unbound attentive may sit at any slot the
		// caller chooses.
		var occurrence = await ResolveSlotAsync(decree, nameof(Decree), decree.Orbit, recurrenceId, cancellationToken);
		var slot = occurrence?.Moment ?? recurrenceId;
		var existing = await context.Attentives.FirstOrDefaultAsync(
			item => item.DecreeId == decree.Id && item.RecurrenceId == slot,
			cancellationToken);
		if (existing is not null)
		{
			return existing;
		}

		var attentive = new Attentive
		{
			DecreeId = decree.Id,
			Epoch = occurrence is not null ? PlaintorchOrbitService.EpochOf(decree, occurrence) : Epoch.FromSlot(slot, PlaintorchOrbitService.ResolveTimeZone(decree)),
			RecurrenceId = slot,
		};
		context.Attentives.Add(attentive);
		return attentive;
	}

	/// <summary>
	/// Ensures the eventive that a dependency endpoint references is hardened into the current unit of work
	/// (no save — the ongoing <see cref="PlainfraContext.SaveChanges()"/> persists it), so a reference to a
	/// projected occurrence makes it a durable row the reconciler can resolve.
	/// </summary>
	/// <remarks>
	/// A reference names one occurrence by its exact slot, and only that slot ever hardens (PEP103, I-035): an already
	/// hardened row at it, or the occurrence of a temporally active fate whose orbit starts one at exactly that moment.
	/// Unlike an interaction, a reference never snaps to the day's first occurrence, since the edge keeps the slot it was
	/// given and endpoints match exactly. A slot that resolves to nothing is left unresolved, and the state judge refuses
	/// the edge naming it (<c>dependency.occurrence</c>), wording why with <see cref="DescribeUnresolvedReference"/>. A
	/// reference hardens the occurrence even when it is dependency-gated: the row must exist for the reference to resolve,
	/// and the lock still applies to whether it can begin.
	/// </remarks>
	/// <returns>
	/// The eventive the endpoint names, or <see langword="null"/> for a non-eventive endpoint and for a slot that resolves to
	/// no occurrence.
	/// </returns>
	public async Task<Eventive?> EnsureReferencedEventiveAsync(EndpointRef endpoint, CancellationToken cancellationToken = default)
	{
		if (endpoint.Kind != DependencyEndpointKind.Eventive || endpoint.RecurrenceId is not { } recurrenceId)
		{
			return null;
		}

		if (await FindEventiveAsync(endpoint.Id, recurrenceId, cancellationToken) is { } existing)
		{
			return existing;
		}

		var fate = await context.Fates.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(item => item.Id == endpoint.Id, cancellationToken);
		if (fate is null || !TemporalActivation.IsActive(fate.Status) || string.IsNullOrWhiteSpace(fate.Orbit))
		{
			return null;
		}

		var dayOccurrences = await orbitService.PreviewDayOccurrencesAsync(fate, fate.Orbit, DateOnly.FromDateTime(recurrenceId), cancellationToken);
		if (dayOccurrences.FirstOrDefault(item => item.Moment == recurrenceId) is not { } occurrence)
		{
			return null;
		}

		var eventive = NewEventive(fate, occurrence.Moment, occurrence);
		context.Eventives.Add(eventive);
		return eventive;
	}

	/// <summary>
	/// Words why a dependency's eventive endpoint resolves to no row, as <see cref="EnsureReferencedEventiveAsync"/> leaves
	/// it (PEP103, I-035). The state judge's <c>dependency.occurrence</c> refusal and the vault's one-time release of the
	/// stored edges that never resolved share it, so both say the same thing.
	/// </summary>
	/// <param name="fate">The fate the endpoint names, or <see langword="null"/> when nothing has its id.</param>
	/// <param name="endpoint">The eventive endpoint.</param>
	/// <returns>
	/// A short reason code (<c>owner-missing</c>, <c>no-slot</c>, <c>fate-stopped</c>, <c>no-orbit</c> or
	/// <c>no-occurrence</c>) for the record, and a sentence for the user.
	/// </returns>
	public static (string Reason, string Message) DescribeUnresolvedReference(Fate? fate, EndpointRef endpoint)
	{
		const string never = "a dependency on it could never be satisfied.";
		if (fate is null)
		{
			return ("owner-missing", $"A dependency cannot name an occurrence of '{endpoint.Id}': no fate has that id.");
		}

		var name = string.IsNullOrWhiteSpace(fate.Title) ? $"Fate '{fate.Id}'" : $"Fate '{fate.Title}'";
		if (endpoint.RecurrenceId is not { } slot)
		{
			return ("no-slot", $"A dependency on an occurrence of {name} must name the occurrence's start; {never}");
		}

		var at = slot.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
		if (!TemporalActivation.IsActive(fate.Status))
		{
			return ("fate-stopped", $"{name} is {fate.Status}, so its occurrence at {at} never comes; {never}");
		}

		return string.IsNullOrWhiteSpace(fate.Orbit)
			? ("no-orbit", $"{name} has no orbit, so nothing of it occurs at {at}; {never}")
			: ("no-occurrence", $"{name} has no occurrence starting at {at}; {never}");
	}

	private async Task<Eventive> EnsureFateEventiveIntoContextAsync(Fate fate, DateTime recurrenceId, CancellationToken cancellationToken)
	{
		// A fate whose temporal activation is off (stopped, PEP103 patch 6) generates nothing and cannot be interacted
		// with. An opted-out fate still honours the user-interaction rule (PEP100/PEP111): interacting with one of its
		// occurrences hardens it, stamped OptOut so it stays hidden until the row is explicitly opted back in by setting it
		// Pending. An occurrence it hardened before it was switched off stays and can still be resolved (user ruling, PEP103
		// patch 6), so a row already hardened at exactly that slot is handed back first — a Passed fate's, which passing
		// hardened, among them (PEP103 I-191).
		if (!TemporalActivation.IsActive(fate.Status) && await FindEventiveAsync(fate.Id, recurrenceId, cancellationToken) is { } kept)
		{
			return kept;
		}

		if (!TemporalActivation.IsActive(fate.Status))
		{
			throw new InvalidOperationException($"Fate '{fate.Id}' is {fate.Status} and does not materialize eventives.");
		}

		var occurrence = await ResolveSlotAsync(fate, nameof(Fate), fate.Orbit, recurrenceId, cancellationToken);
		var slot = occurrence?.Moment ?? recurrenceId;

		// PEP101: a locked whole-fate freezes all materialization; a locked single occurrence blocks just itself.
		if (await dependencyGate.IsFateMaterializationBlockedAsync(fate.Id, slot, cancellationToken))
		{
			throw new InvalidOperationException($"Fate '{fate.Id}' occurrence on {slot:yyyy-MM-dd} is blocked by unmet dependencies and cannot be materialized.");
		}

		var existing = await FindEventiveAsync(fate.Id, slot, cancellationToken);
		if (existing is not null)
		{
			return existing;
		}

		var eventive = NewEventive(fate, slot, occurrence);
		context.Eventives.Add(eventive);
		return eventive;
	}

	/// <summary>
	/// Builds the row a fate's occurrence hardens into at <paramref name="slot"/>, positioned at the orbit occurrence when
	/// there is one. An opted-out fate's rows are stamped OptOut, so they stay hidden until opted back in.
	/// </summary>
	private static Eventive NewEventive(Fate fate, DateTime slot, OrbitOccurrenceInstance? occurrence) => new()
	{
		FateId = fate.Id,
		Epoch = occurrence is not null ? PlaintorchOrbitService.EpochOf(fate, occurrence) : Epoch.FromSlot(slot, PlaintorchOrbitService.ResolveTimeZone(fate)),
		RecurrenceId = slot,
		Resolution = fate.Status == FateStatus.OptOut ? EventiveResolution.OptOut : EventiveResolution.Pending,
	};

	/// <summary>
	/// Resolves, in preview mode, the orbit occurrence a RECURRENCE-ID addresses for an interaction: the occurrence
	/// starting at exactly that moment, or the day's first occurrence when none does, so a client may address an
	/// occurrence by its day alone. <see langword="null"/> when the declarative has no orbit; throws when its orbit has no
	/// occurrence on that day. A dependency reference never snaps (<see cref="EnsureReferencedEventiveAsync"/>).
	/// </summary>
	private async Task<OrbitOccurrenceInstance?> ResolveSlotAsync(Incentive owner, string ownerKind, string? orbit, DateTime recurrenceId, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(orbit))
		{
			return null;
		}

		var day = DateOnly.FromDateTime(recurrenceId);
		var dayOccurrences = await orbitService.PreviewDayOccurrencesAsync(owner, orbit, day, cancellationToken);
		if (dayOccurrences.Count == 0)
		{
			throw new InvalidOperationException($"{ownerKind} '{owner.Id}' has no orbit occurrence on {day:yyyy-MM-dd}.");
		}

		return dayOccurrences.FirstOrDefault(item => item.Moment == recurrenceId) ?? dayOccurrences[0];
	}

	/// <summary>
	/// Finds a fate's eventive for a RECURRENCE-ID, seeing both a row already added in the current unit of work (so two
	/// references in one save do not duplicate it) and the database.
	/// </summary>
	private async Task<Eventive?> FindEventiveAsync(string fateId, DateTime recurrenceId, CancellationToken cancellationToken)
	{
		var tracked = context.ChangeTracker.Entries<Eventive>()
			.FirstOrDefault(entry => entry.State == EntityState.Added
				&& entry.Entity.FateId == fateId
				&& entry.Entity.RecurrenceId == recurrenceId)?.Entity;
		if (tracked is not null)
		{
			return tracked;
		}

		return await context.Eventives.FirstOrDefaultAsync(item => item.FateId == fateId && item.RecurrenceId == recurrenceId, cancellationToken);
	}
}
