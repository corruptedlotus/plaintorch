using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Plaintorch.Materialization;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Enforcement;

/// <summary>
/// How a fate passes and comes back (PEP103 I-191, user ruling): <see cref="FateStatus.Passed"/> is inferred and selected
/// alike. The engine moves a running fate to Passed once it has fully passed, and moves an inferred pass back once the user's
/// edit gives it an occurrence still to close; a pass selected by hand acts like Stopped, and only an occurrence added by hand
/// that is still to close brings it back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fully passed</b> (<see cref="FullyPassedAsync"/>): its orbit yields no occurrence whose window closes after now, and every
/// hardened eventive — added by hand or born of the orbit, however resolved — has a window that has closed. A fate with neither
/// an orbit nor an eventive has nothing to pass, and never does. An orbit that no longer resolves cannot be told, so the
/// engine leaves its fate as it is (PEP103 I-124).
/// </para>
/// <para>
/// <b>Passing</b>, evaluated on a save for each running fate (Active or OptOut) the save creates, whose orbit or status it
/// changes, or whose eventives it adds, changes or removes — so the move is judged and recorded with the save — and by the
/// hourly rolling pass for the fates that pass by time alone (<see cref="PassElapsedAsync"/>; PEP103 I-070 accepts that lag).
/// A stopped fate never passes. The move is the engine's own, made only where the lifecycle rules allow it as the save stands
/// (<see cref="StateJudge.AllowsAsync"/>, as the moves on joining are, Patch103.3), so a fate held by a finish edge into it
/// keeps running and the engine never compensates for its own move. Before passing, what time brought of its orbit hardens
/// (<see cref="ProximityMaterializationService.HardenElapsedAsync"/>), so the occurrences it went through stay as history once
/// its projection stops. The fate remembers the started status it left (<see cref="Fate.PassedFrom"/>) and that its pass was
/// inferred (<see cref="Fate.PassInferred"/>).
/// </para>
/// <para>
/// <b>Coming back</b>, for a fate the save leaves Passed: an occurrence added by hand whose window closes after now brings back
/// either kind of pass; an inferred pass also comes back when the save changes its orbit or its eventives and leaves it no
/// longer fully passed (an orbit begetting an occurrence still to come, an eventive moved ahead). It returns to the started
/// status it left — Active for one that passed from Stopped. The move back is the user's edit's: it is judged under the reopen
/// rule like any move back (Patch103.4), so it cascades under Auto, waits under Warn and is refused under Block, and the edit
/// with it. A compensation moving a Passed fate back returns it to the same status (<see cref="StateJudge"/>).
/// </para>
/// <para>
/// <b>Settling</b> (<see cref="SettleAsync"/>, first in the outcome phase so a compensation is read too): the markers follow the
/// status the save leaves — a fate not Passed carries none, and one entering Passed by any other way than inference (by hand,
/// from a note, or as a compensation) is a selected pass, remembering the started status it left — and a fate the engine
/// brought back, here or as a compensation, seeks its cursor to now as a resume does, so nothing it passed while Passed
/// hardens. One a new orbit brought back keeps the fresh cursor its reset anchored, and one the user moved back was sought
/// already with the resumes (<see cref="StructureReactions"/>).
/// </para>
/// Context-bound services are resolved from the scope when needed, for the reason <see cref="StateEngine"/> gives.
/// </remarks>
public sealed class FateReactions(StateJudge judge, IServiceProvider serviceProvider)
{
	private static readonly IReadOnlyList<FateStatus> StartedStatuses = LifecycleDeclarations.MembersOf(typeof(FateStatus))
		.Where(member => member.State == LifecycleState.Started)
		.Select(member => (FateStatus)member.Status)
		.ToList();

	/// <summary>
	/// Whether a fate takes an occurrence added by hand at <paramref name="epoch"/>: a running fate takes any; a fate whose
	/// pass was inferred takes any too, one still to close bringing it back; a selected pass takes only one whose window closes
	/// after <paramref name="now"/>, which brings it back; a stopped fate takes none (PEP103 I-191).
	/// </summary>
	public static bool TakesManualEventive(Fate fate, Epoch epoch, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(fate);
		ArgumentNullException.ThrowIfNull(epoch);
		return TemporalActivation.IsActive(fate.Status)
			|| (fate.Status == FateStatus.Passed && (fate.PassInferred || epoch.ClosesAt() > now));
	}

	/// <summary>
	/// Applies the passages to the save: each fate it touches comes back or passes, as it now stands. Runs last in the
	/// structure phase, on the orbits, cursors and occurrences the reactions before it leave, so the judge sees the statuses
	/// it sets.
	/// </summary>
	public async Task ApplyAsync(StatePass pass, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(pass);
		var context = pass.Context;
		var touched = await TouchedAsync(context, cancellationToken);
		if (touched.Count == 0)
		{
			return;
		}

		var now = DateTimeOffset.Now;
		foreach (var touch in touched)
		{
			var fate = touch.Fate;
			if (touch.StoredStatus == FateStatus.Passed && fate.Status == FateStatus.Passed)
			{
				if (touch.AddsOpenManual(now))
				{
					ComeBack(pass, fate, "manual-occurrence");
				}
				else if (fate.PassInferred
					&& (touch.OrbitChanged || touch.Eventives.Count > 0)
					&& await FullyPassedAsync(context, fate, now, cancellationToken) == false)
				{
					ComeBack(pass, fate, touch.OrbitChanged ? "orbit" : "eventive");
				}

				continue;
			}

			if (StartedStatuses.Contains(fate.Status)
				&& (touch.Created || touch.OrbitChanged || touch.StatusChanged || touch.Eventives.Count > 0))
			{
				await TryPassAsync(context, fate, now, pass, cancellationToken);
			}
		}
	}

	/// <summary>
	/// The rolling pass's share (PEP103 I-191): every running fate that has fully passed by <paramref name="now"/> passes, where
	/// nothing blocks it, on the unit of work the pass is about to save, so the engine judges and records each move and its note
	/// follows once the save commits. A fate whose orbit still caches a next occurrence has one ahead and is skipped; the pass
	/// has refreshed that cache on the tracked fates just before. Existing fates whose orbits ran out before Passed existed pass
	/// together on the first pass after an upgrade. Returns how many fates passed.
	/// </summary>
	public async Task<int> PassElapsedAsync(PlainfraContext context, DateTimeOffset now, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		// A tracking query hands back the instances the pass already tracks, with the next occurrence it just computed.
		var fates = await context.Fates
			.IgnoreAutoIncludes()
			.Where(fate => StartedStatuses.Contains(fate.Status) && (fate.Orbit != null || fate.Eventives.Any()))
			.ToListAsync(cancellationToken);
		var passed = 0;
		foreach (var fate in fates)
		{
			if (!string.IsNullOrWhiteSpace(fate.Orbit) && fate.NextOccurrence is not null)
			{
				continue;
			}

			passed += await TryPassAsync(context, fate, now, pass: null, cancellationToken) ? 1 : 0;
		}

		return passed;
	}

	/// <summary>
	/// Whether a fate has fully passed by <paramref name="now"/>, read on the unit of work as it stands: its orbit yields no
	/// occurrence still to close (<see cref="PlaintorchOrbitService.HasOpenOccurrenceAsync"/>) and each of its hardened
	/// eventives has closed (<see cref="Epoch.ClosesAt"/>). <see langword="false"/> for a fate with neither an orbit nor an
	/// eventive, and <see langword="null"/> when its orbit no longer resolves, so it cannot be told.
	/// </summary>
	public async Task<bool?> FullyPassedAsync(PlainfraContext context, Fate fate, DateTimeOffset now, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(fate);
		var hardened = await HardenedAsync(context, fate.Id, cancellationToken);
		var orbit = string.IsNullOrWhiteSpace(fate.Orbit) ? null : fate.Orbit;
		if ((orbit is null && hardened.Count == 0) || hardened.Any(eventive => eventive.Epoch.ClosesAt() > now))
		{
			return false;
		}

		if (orbit is null)
		{
			return true;
		}

		var slots = hardened
			.Where(eventive => eventive.RecurrenceId is not null)
			.Select(eventive => eventive.RecurrenceId!.Value)
			.ToHashSet();
		var orbitService = serviceProvider.GetRequiredService<PlaintorchOrbitService>();
		var open = false;
		var resolved = await orbitService.IsolateAsync(fate, "a fate's passing", async () =>
			open = await orbitService.HasOpenOccurrenceAsync(fate, orbit, now, slots, cancellationToken));
		return resolved ? !open : null;
	}

	/// <summary>
	/// Settles every fate the save leaves (PEP103 I-191), first in the outcome phase so a compensation is read too.
	/// <list type="bullet">
	/// <item><description>Its passage markers follow its status: a fate that is not Passed carries none, an inferred pass keeps
	/// its own, and a fate entering Passed by any other way — by hand, from a note, or as a compensation — is a selected pass,
	/// remembering the started status it left (none when it left Stopped).</description></item>
	/// <item><description>A fate the engine brought back from Passed — here or as a compensation — seeks its cursor to now as a
	/// resume does, unless the save gives it a new orbit, whose reset cursor it keeps. One the user moved back was sought with
	/// the resumes already.</description></item>
	/// </list>
	/// </summary>
	public async Task SettleAsync(StatePass pass, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(pass);
		var now = DateTimeOffset.Now;
		foreach (var entry in pass.Context.ChangeTracker.Entries<Fate>().Where(entry => entry.State is EntityState.Added or EntityState.Modified).ToList())
		{
			var fate = entry.Entity;
			if (fate.Status == FateStatus.Passed && fate.PassInferred)
			{
				// An inferred pass is the engine's, made in this save; outside one, the flag stands only while the fate is Passed.
				continue;
			}

			var stored = entry.State == EntityState.Added ? null : await entry.GetDatabaseValuesAsync(cancellationToken);
			var storedStatus = stored?[nameof(Fate.Status)] as FateStatus?;
			if (fate.Status == FateStatus.Passed)
			{
				if (storedStatus != FateStatus.Passed)
				{
					fate.PassedFrom = storedStatus is { } from && StartedStatuses.Contains(from) ? from : null;
				}

				continue;
			}

			if (fate.PassInferred)
			{
				fate.PassInferred = false;
			}

			if (fate.PassedFrom is not null)
			{
				fate.PassedFrom = null;
			}

			if (storedStatus == FateStatus.Passed
				&& TemporalActivation.IsActive(fate.Status)
				&& pass.Moved.Any(moved => ReferenceEquals(moved, fate))
				&& fate.Orbit is { Length: > 0 } orbit
				&& string.Equals(stored![nameof(Fate.Orbit)] as string, orbit, StringComparison.Ordinal))
			{
				var orbitService = serviceProvider.GetRequiredService<PlaintorchOrbitService>();
				await orbitService.IsolateAsync(fate, "a fate coming back", () => orbitService.FastForwardToNowAsync(fate, orbit, now, cancellationToken));
			}
		}
	}

	/// <summary>
	/// Passes a running fate that has fully passed, where nothing blocks the move: what time brought of its orbit hardens
	/// first, then it takes Passed, remembering the started status it left. Returns whether it passed.
	/// </summary>
	private async Task<bool> TryPassAsync(PlainfraContext context, Fate fate, DateTimeOffset now, StatePass? pass, CancellationToken cancellationToken)
	{
		if (await FullyPassedAsync(context, fate, now, cancellationToken) != true
			|| !await judge.AllowsAsync(context, fate, FateStatus.Passed, cancellationToken))
		{
			return false;
		}

		var orbitService = serviceProvider.GetRequiredService<PlaintorchOrbitService>();
		var materialization = serviceProvider.GetRequiredService<ProximityMaterializationService>();
		if (!await orbitService.IsolateAsync(fate, "a fate's passing", async () => await materialization.HardenElapsedAsync(fate, now, cancellationToken)))
		{
			return false;
		}

		var from = fate.Status;
		fate.Status = FateStatus.Passed;
		fate.PassedFrom = from;
		fate.PassInferred = true;
		serviceProvider.GetRequiredService<VaultAuditLogService>().Stage(
			"state",
			"state.pass",
			subject: fate,
			details: new { from = from.ToString(), status = nameof(FateStatus.Passed) });
		pass?.Move(fate);
		return true;
	}

	/// <summary>
	/// Brings a Passed fate back to the started status it left, Active when it left Stopped. Its cursor is sought once the
	/// save is judged (<see cref="SettleAsync"/>).
	/// </summary>
	private void ComeBack(StatePass pass, Fate fate, string cause)
	{
		var to = fate.PassedFrom is { } from && StartedStatuses.Contains(from) ? from : LifecycleDeclarations.InitialStatus<FateStatus>();
		var inferred = fate.PassInferred;
		fate.Status = to;
		fate.PassedFrom = null;
		fate.PassInferred = false;
		serviceProvider.GetRequiredService<VaultAuditLogService>().Stage(
			"state",
			"state.revert",
			subject: fate,
			details: new { cause, inferred, from = nameof(FateStatus.Passed), status = to.ToString() });
		pass.Move(fate);
	}

	/// <summary>
	/// The fates the save touches, each with what the save does to it: those it creates or changes, and the owners of the
	/// eventives it adds, changes or removes, loaded when the save does not hold them.
	/// </summary>
	private static async Task<List<FateTouch>> TouchedAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var eventives = context.ChangeTracker.Entries<Eventive>()
			.Where(Changed)
			.ToList();
		var fates = context.ChangeTracker.Entries<Fate>()
			.Where(entry => entry.State is EntityState.Added or EntityState.Modified
				|| (entry.State == EntityState.Unchanged && eventives.Any(eventive => SameId(eventive.Entity.FateId, entry.Entity.Id))))
			.Select(entry => entry.Entity)
			.ToList();
		foreach (var fateId in eventives.Select(entry => entry.Entity.FateId).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
		{
			if (!fates.Any(fate => SameId(fate.Id, fateId))
				&& !context.ChangeTracker.Entries<Fate>().Any(entry => SameId(entry.Entity.Id, fateId))
				&& await context.Fates.IgnoreAutoIncludes().FirstOrDefaultAsync(fate => fate.Id == fateId, cancellationToken) is { } loaded)
			{
				fates.Add(loaded);
			}
		}

		var touched = new List<FateTouch>(fates.Count);
		foreach (var fate in fates)
		{
			var entry = context.Entry(fate);
			if (entry.State is EntityState.Deleted or EntityState.Detached)
			{
				continue;
			}

			var stored = entry.State == EntityState.Added ? null : await entry.GetDatabaseValuesAsync(cancellationToken);
			var storedStatus = stored?[nameof(Fate.Status)] as FateStatus?;
			touched.Add(new FateTouch(
				fate,
				storedStatus,
				Created: stored is null,
				StatusChanged: storedStatus != fate.Status,
				OrbitChanged: stored is null
					? !string.IsNullOrWhiteSpace(fate.Orbit)
					: !string.Equals(stored[nameof(Fate.Orbit)] as string, fate.Orbit, StringComparison.Ordinal),
				eventives.Where(eventive => SameId(eventive.Entity.FateId, fate.Id)).ToList()));
		}

		return touched;
	}

	/// <summary>
	/// A fate's hardened eventives as the save leaves them: the stored ones, with those the save changes read as it leaves
	/// them, those it removes gone and those it adds counted.
	/// </summary>
	private static async Task<List<Eventive>> HardenedAsync(PlainfraContext context, string fateId, CancellationToken cancellationToken)
	{
		var stored = await context.Eventives
			.AsNoTracking()
			.Where(eventive => eventive.FateId == fateId)
			.ToListAsync(cancellationToken);
		var byId = stored.ToDictionary(eventive => eventive.Id);
		var added = new List<Eventive>();
		foreach (var entry in context.ChangeTracker.Entries<Eventive>().Where(entry => SameId(entry.Entity.FateId, fateId)))
		{
			switch (entry.State)
			{
				case EntityState.Deleted:
					byId.Remove(entry.Entity.Id);
					break;
				case EntityState.Added:
					added.Add(entry.Entity);
					break;
				case EntityState.Modified or EntityState.Unchanged:
					byId[entry.Entity.Id] = entry.Entity;
					break;
			}
		}

		return [.. byId.Values, .. added];
	}

	/// <summary>
	/// Whether the save adds, changes or removes an eventive. Its position in time is an owned value with an entry of its own,
	/// so moving it leaves the eventive's entry unchanged (the blind spot PEP103 I-196 records); the epoch's entry is read too.
	/// </summary>
	private static bool Changed(EntityEntry<Eventive> entry)
		=> entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
			|| entry.Reference(eventive => eventive.Epoch).TargetEntry?.State is EntityState.Added or EntityState.Modified or EntityState.Deleted;

	private static bool SameId(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// One fate a save touches, and what it does to it.
	/// </summary>
	/// <param name="Fate">The fate, as the save tracks it.</param>
	/// <param name="StoredStatus">Its status as stored, or <see langword="null"/> for one the save creates.</param>
	/// <param name="Created">Whether the save creates it.</param>
	/// <param name="StatusChanged">Whether the save stores another status than the one stored.</param>
	/// <param name="OrbitChanged">Whether the save sets, changes or clears its orbit (a new fate's counts when it has one).</param>
	/// <param name="Eventives">The entries of its eventives the save adds, changes or removes.</param>
	private sealed record FateTouch(
		Fate Fate,
		FateStatus? StoredStatus,
		bool Created,
		bool StatusChanged,
		bool OrbitChanged,
		IReadOnlyList<EntityEntry<Eventive>> Eventives)
	{
		/// <summary>
		/// Whether the save adds an occurrence by hand whose window closes after <paramref name="now"/>.
		/// </summary>
		public bool AddsOpenManual(DateTimeOffset now)
			=> Eventives.Any(entry => entry.State == EntityState.Added && entry.Entity.IsManual && entry.Entity.Epoch.ClosesAt() > now);
	}
}
