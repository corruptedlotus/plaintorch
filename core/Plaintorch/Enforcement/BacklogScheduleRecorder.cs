using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Enforcement;

/// <summary>
/// Keeps the backlog schedules of a unit of work coherent (PEP101). The last phase of the <see cref="StateEngine"/>, which
/// <see cref="StateInterceptor"/> runs on every save, so every write pathway — the API, the markdown watcher, the CLI, a
/// background pass, synchronous or not — records lifecycle transitions and replaces occupied slots the same way. It reads the
/// statuses the save leaves behind: the compensations the engine made and the checkpoints it unlocked included.
/// </summary>
/// <remarks>
/// Two rules, applied in order:
/// <list type="number">
/// <item><description>
/// <b>Recording.</b> For every incentive, directive or checkpoint the save touches, its lifecycle state as stored is
/// compared with the state the save is about to store (<see cref="EntityLifecycleResolver.StateOf(object)"/>), and records
/// track every state other than Open (PEP103):
/// <list type="bullet">
/// <item><description>Moving up the main line adds a <see cref="BacklogScheduleType.Record"/> of each state it reaches,
/// stamped with the save's moment: a jump from Open straight to Finished records Started and Finished at the same moment.
/// Moving back down withdraws the records of the states it leaves, so reopening drops the Finished record.</description></item>
/// <item><description>Deferring adds a Deferred record and keeps the Started and Finished records: a deferred item keeps its
/// place on the main line. Coming back withdraws the Deferred record, and the move is read from that kept place, which the
/// records themselves show, so resuming to where the item was changes nothing else.</description></item>
/// </list>
/// An entity created in a state counts as entering it. Only the states an owner's kind passes through are recorded: a
/// checkpoint never starts, and a fate or a decree never finishes. A state that changes with time alone (a checkpoint whose
/// due passes) reads the same stored and current, so it is recorded only once the engine's clock re-evaluates it.
/// <para>
/// A kind that keeps a history (<see cref="EntityLifecycleResolver.KeepsHistory"/>: a fate, a decree, a lunar directive)
/// has no place on a main line to keep (PEP103 patch 6): every state it enters is recorded anew, at the save's moment, and
/// nothing is withdrawn, so it may hold several records of one state.
/// </para>
/// </description></item>
/// <item><description>
/// <b>Replacement.</b> Every schedule added in the save replaces the other schedules of its slot — the same owner,
/// type and state — whether they are stored or tracked. Two additions to one slot in a single save keep the later
/// one. A history record replaces nothing.
/// </description></item>
/// </list>
/// </remarks>
public sealed class BacklogScheduleRecorder(EntityLifecycleResolver lifecycleResolver)
{
	/// <summary>
	/// Records the lifecycle transitions of the unit of work, then settles every slot a schedule is added to or withdrawn
	/// from.
	/// </summary>
	public async Task ApplyAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		// A record is a civil wall-clock moment, like every schedule time: the local time with no kind, which would
		// otherwise serialize with the machine's offset. One moment for the whole save, so a jump straight to a finished
		// status records its start and its finish at the same instant.
		var history = new HashSet<BacklogSchedule>(ReferenceEqualityComparer.Instance);
		var withdrawn = await RecordLifecycleTransitionsAsync(context, DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified), history, cancellationToken);
		await SettleSlotsAsync(context, withdrawn, history, cancellationToken);
	}

	/// <summary>
	/// Adds a record for every state an owner enters in this save, and returns the slots of the records to withdraw for
	/// every state an owner leaves. The records of owners that keep a history go into <paramref name="history"/>.
	/// </summary>
	private async Task<List<BacklogSchedule>> RecordLifecycleTransitionsAsync(PlainfraContext context, DateTime now, ISet<BacklogSchedule> history, CancellationToken cancellationToken)
	{
		var entered = new List<BacklogSchedule>();
		var withdrawn = new List<BacklogSchedule>();
		// A snapshot: reading the stored values below must not enumerate the tracker while it is being walked.
		var owners = context.ChangeTracker.Entries()
			.Where(entry => entry.State is (EntityState.Added or EntityState.Modified) && BacklogSchedule.CanOwn(entry.Entity))
			.ToList();
		foreach (var entry in owners)
		{
			var kind = entry.Entity.GetType();
			var states = lifecycleResolver.StatesOf(kind);
			if (states.Count == 0)
			{
				continue;
			}

			// The entity as it stands in the database, read onto a detached copy that the same resolver judges. The stored
			// row, not the tracked original values, is the reference: an entity attached rather than loaded (whose original
			// values are only its current ones) is compared correctly all the same. An added entity has nothing stored.
			var stored = entry.State == EntityState.Added ? null : await entry.GetDatabaseValuesAsync(cancellationToken);
			LifecycleState? before = stored is null ? null : lifecycleResolver.StateOf(stored.ToObject());
			var after = lifecycleResolver.StateOf(entry.Entity);
			if (before == after)
			{
				continue;
			}

			if (lifecycleResolver.KeepsHistory(kind))
			{
				if (states.Contains(after))
				{
					var record = Record(entry.Entity, after, now);
					entered.Add(record);
					history.Add(record);
				}

				continue;
			}

			if (after == LifecycleState.Deferred)
			{
				// Setting the item aside keeps its place on the main line, and with it the records of how far it got.
				if (states.Contains(LifecycleState.Deferred))
				{
					entered.Add(Record(entry.Entity, LifecycleState.Deferred, now));
				}

				continue;
			}

			var from = before ?? LifecycleState.Open;
			if (from == LifecycleState.Deferred)
			{
				withdrawn.Add(Record(entry.Entity, LifecycleState.Deferred, now));
				from = await MainLinePositionAsync(context, entry.Entity, cancellationToken);
			}

			var transition = lifecycleResolver.Transition(kind, from, after);
			entered.AddRange(transition.Entered.Select(state => Record(entry.Entity, state, now)));
			withdrawn.AddRange(transition.Left.Select(state => Record(entry.Entity, state, now)));
		}

		context.BacklogSchedules.AddRange(entered);
		return withdrawn;
	}

	/// <summary>
	/// Reads where a deferred owner stood on the main line before it was set aside, from the records it kept: Finished with a
	/// Finished record, Started with only a Started one, and Open with neither.
	/// </summary>
	private static async Task<LifecycleState> MainLinePositionAsync(PlainfraContext context, object owner, CancellationToken cancellationToken)
	{
		var ownerId = Record(owner, LifecycleState.Deferred, default).OwnerId;
		var recorded = await context.BacklogSchedules
			.AsNoTracking()
			.Where(schedule => schedule.Type == BacklogScheduleType.Record
				&& (schedule.IncentiveId == ownerId || schedule.DirectiveId == ownerId || schedule.CheckpointId == ownerId))
			.Select(schedule => schedule.State)
			.ToListAsync(cancellationToken);
		return recorded.Contains(LifecycleState.Finished) ? LifecycleState.Finished
			: recorded.Contains(LifecycleState.Started) ? LifecycleState.Started
			: LifecycleState.Open;
	}

	private static BacklogSchedule Record(object owner, LifecycleState state, DateTime now)
		=> BacklogSchedule.For(owner, BacklogScheduleType.Record, state, now);

	/// <summary>
	/// Replaces every slot an addition lands in and empties every withdrawn slot, over stored and tracked schedules alike.
	/// A history record takes no slot: it neither replaces nor is replaced.
	/// </summary>
	private static async Task SettleSlotsAsync(PlainfraContext context, IReadOnlyList<BacklogSchedule> withdrawn, IReadOnlySet<BacklogSchedule> history, CancellationToken cancellationToken)
	{
		var added = context.ChangeTracker.Entries<BacklogSchedule>()
			.Where(entry => entry.State == EntityState.Added)
			.Select(entry => entry.Entity)
			.ToList();
		if (added.Count == 0 && withdrawn.Count == 0)
		{
			return;
		}

		// Loading brings the stored rows of the touched owners into the tracker, so the settling below runs over one view
		// of stored and pending schedules alike (a row already tracked is returned as that tracked instance).
		var touched = added.Concat(withdrawn).ToList();
		var incentiveIds = OwnerIds(touched, schedule => schedule.IncentiveId);
		var directiveIds = OwnerIds(touched, schedule => schedule.DirectiveId);
		var checkpointIds = OwnerIds(touched, schedule => schedule.CheckpointId);
		await context.BacklogSchedules
			.Where(schedule => (schedule.IncentiveId != null && incentiveIds.Contains(schedule.IncentiveId))
				|| (schedule.DirectiveId != null && directiveIds.Contains(schedule.DirectiveId))
				|| (schedule.CheckpointId != null && checkpointIds.Contains(schedule.CheckpointId)))
			.LoadAsync(cancellationToken);

		// The later addition to a slot wins; the tracker lists additions in the order they were made.
		var winners = new List<BacklogSchedule>();
		foreach (var schedule in added.Where(schedule => !history.Contains(schedule)))
		{
			winners.RemoveAll(winner => winner.SharesSlotWith(schedule));
			winners.Add(schedule);
		}

		foreach (var entry in context.ChangeTracker.Entries<BacklogSchedule>().ToList())
		{
			if (entry.State == EntityState.Deleted)
			{
				continue;
			}

			var schedule = entry.Entity;
			if (history.Contains(schedule))
			{
				continue;
			}

			// A withdrawn slot empties whatever stands in it: a record never outlives its state.
			var inWithdrawnSlot = withdrawn.Any(slot => slot.SharesSlotWith(schedule));
			var replaced = !winners.Contains(schedule) && winners.Any(winner => winner.SharesSlotWith(schedule));
			if (inWithdrawnSlot || replaced)
			{
				// An addition that is dropped in the same save is simply never inserted; a stored row is deleted.
				entry.State = entry.State == EntityState.Added ? EntityState.Detached : EntityState.Deleted;
			}
		}
	}

	private static List<string> OwnerIds(IEnumerable<BacklogSchedule> schedules, Func<BacklogSchedule, string?> ownerId)
	{
		return schedules
			.Select(ownerId)
			.OfType<string>()
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}
}
