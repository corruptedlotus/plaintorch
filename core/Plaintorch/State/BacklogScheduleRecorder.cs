using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.State;

/// <summary>
/// Keeps the backlog schedules of a unit of work coherent (PEP101). Runs inside the state-policy pass, and therefore
/// the save interceptor, so every write pathway — the API, the markdown watcher, the CLI, a bare context save — records
/// lifecycle transitions and replaces occupied slots the same way.
/// </summary>
/// <remarks>
/// Two rules, applied in order:
/// <list type="number">
/// <item><description>
/// <b>Recording.</b> For every incentive, directive or checkpoint the save touches, each lifecycle phase its kind passes
/// through (<see cref="EntityLifecycleResolver.GetPhases"/>) is compared before and after the save. Entering the phase adds
/// a <see cref="BacklogScheduleType.Record"/> of it, stamped with the save's moment; leaving it — pulling the status back —
/// withdraws that record. So a record stands exactly while its phase does: un-finishing drops the finish record,
/// un-beginning the begin record. Begun and finished are the resolver's reading, the same one the dependency system gates
/// on: a finish implies a begin, so a jump from an unbegun status straight to a finished one records both at the same
/// moment, and a status that declares no phase (an objective's Blocked) is not begun. An entity created begun or finished
/// counts as entering it. A one-shot kind (a checkpoint, which finishes by unlocking) only ever gets a finish record, and
/// kinds with no lifecycle (lunar directives, decrees) are never recorded, though every kind can hold plans.
/// </description></item>
/// <item><description>
/// <b>Replacement.</b> Every schedule added in the save replaces the other schedules of its slot — the same owner,
/// type and phase — whether they are stored or tracked. Two additions to one slot in a single save keep the later
/// one.
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
		// status records its begin and its finish at the same instant.
		var withdrawn = RecordLifecycleTransitions(context, DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified));
		await SettleSlotsAsync(context, withdrawn, cancellationToken);
	}

	/// <summary>
	/// Adds a record for every phase an owner enters in this save, and returns the slots of the records to withdraw for
	/// every phase an owner leaves.
	/// </summary>
	private List<BacklogSchedule> RecordLifecycleTransitions(PlainfraContext context, DateTime now)
	{
		var entered = new List<BacklogSchedule>();
		var withdrawn = new List<BacklogSchedule>();
		foreach (var entry in context.ChangeTracker.Entries())
		{
			if (entry.State is not (EntityState.Added or EntityState.Modified) || !BacklogSchedule.CanOwn(entry.Entity))
			{
				continue;
			}

			var phases = lifecycleResolver.GetPhases(entry.Entity.GetType());
			if (phases.Count == 0)
			{
				continue;
			}

			// The entity as it stood before the save: its original values on a detached copy, read by the same resolver.
			var before = entry.State == EntityState.Added ? null : entry.OriginalValues.ToObject();
			foreach (var phase in phases)
			{
				var was = before is not null && HasReached(before, phase);
				var @is = HasReached(entry.Entity, phase);
				if (@is && !was)
				{
					entered.Add(BacklogSchedule.For(entry.Entity, BacklogScheduleType.Record, phase, now));
				}
				else if (was && !@is)
				{
					withdrawn.Add(BacklogSchedule.For(entry.Entity, BacklogScheduleType.Record, phase, now));
				}
			}
		}

		// Added after the scan: adding while enumerating the tracker would change the collection under it.
		context.BacklogSchedules.AddRange(entered);
		return withdrawn;
	}

	private bool HasReached(object entity, LifecyclePhase phase)
		=> phase == LifecyclePhase.Begin ? lifecycleResolver.HasBegun(entity) : lifecycleResolver.HasFinished(entity);

	/// <summary>
	/// Replaces every slot an addition lands in and empties every withdrawn slot, over stored and tracked schedules alike.
	/// </summary>
	private static async Task SettleSlotsAsync(PlainfraContext context, IReadOnlyList<BacklogSchedule> withdrawn, CancellationToken cancellationToken)
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
		foreach (var schedule in added)
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
			// A withdrawn slot empties whatever stands in it: a record never outlives its phase.
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
