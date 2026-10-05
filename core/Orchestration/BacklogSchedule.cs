using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Pleiades.Orchestration.Lifecycle;

namespace Pleiades.Orchestration;

/// <summary>
/// One point in a backlog item's schedule (PEP101, PEP103): when an incentive, a directive or a checkpoint reaches a
/// lifecycle state (<see cref="State"/>), either as the user plans it or as it actually turned out (<see cref="Type"/>).
/// It replaces the directive's former start and end dates as the scheduling vocabulary of the backlog.
/// </summary>
/// <remarks>
/// <para>
/// A schedule belongs to exactly one owner, named by one of <see cref="IncentiveId"/>, <see cref="DirectiveId"/> or
/// <see cref="CheckpointId"/>. Each is a real foreign key that cascades, so a schedule dies with its owner, and a check
/// constraint refuses a row naming no owner or several.
/// </para>
/// <para>
/// An owner holds at most one schedule per <see cref="Type"/> and <see cref="State"/> (its slot). Adding a schedule to
/// an occupied slot replaces the one there: the state-policy save hook removes the old row in the same save, whatever
/// the write pathway, and a unique index per owner column is the database backstop.
/// </para>
/// <para>
/// <see cref="BacklogScheduleType.Record"/> schedules are written by the core, never by hand, and track the states other
/// than <see cref="LifecycleState.Open"/>: <see cref="LifecycleState.Started"/>, <see cref="LifecycleState.Finished"/> and
/// <see cref="LifecycleState.Deferred"/>. Reaching Started or Finished adds its record at that moment, and pulling the item
/// back along the main line withdraws the records of the states it leaves. Deferring keeps them, since the item keeps its
/// place on the main line, and adds a Deferred record that stands while it stays deferred. Only the states an owner's kind
/// passes through are recorded: a checkpoint never starts, so it has no Started record.
/// </para>
/// <para>
/// <see cref="BacklogScheduleType.Plan"/> schedules place a Started or a Finished state the owner's kind passes through.
/// Deferral is not planned.
/// </para>
/// </remarks>
public sealed class BacklogSchedule
{
	/// <summary>
	/// Gets or sets the database identity of the schedule.
	/// </summary>
	[Key]
	public long Id { get; set; }

	/// <summary>
	/// Gets or sets where the schedule comes from: the user's plan or the recorded reality.
	/// </summary>
	public BacklogScheduleType Type { get; set; }

	/// <summary>
	/// Gets or sets the lifecycle state the schedule places in time: the owner's start or finish, or for a record also its
	/// deferral. Never <see cref="LifecycleState.Open"/>.
	/// </summary>
	public LifecycleState State { get; set; }

	/// <summary>
	/// Gets or sets the moment of the phase: a civil (wall-clock) moment, like <see cref="Due.Moment"/>, never an
	/// absolute UTC instant. A plan at midnight stands for the whole day; a record carries the local time it was written.
	/// </summary>
	public DateTime Time { get; set; }

	/// <summary>
	/// Gets or sets the owning incentive's identifier (an objective, a fate or a decree), when an incentive owns the
	/// schedule.
	/// </summary>
	public string? IncentiveId { get; set; }

	/// <summary>
	/// Gets or sets the owning incentive.
	/// </summary>
	[ForeignKey(nameof(IncentiveId))]
	public Incentive? Incentive { get; set; }

	/// <summary>
	/// Gets or sets the owning directive's identifier (stellar or lunar), when a directive owns the schedule.
	/// </summary>
	public string? DirectiveId { get; set; }

	/// <summary>
	/// Gets or sets the owning directive.
	/// </summary>
	[ForeignKey(nameof(DirectiveId))]
	public Directive? Directive { get; set; }

	/// <summary>
	/// Gets or sets the owning checkpoint's identifier, when a checkpoint owns the schedule.
	/// </summary>
	public string? CheckpointId { get; set; }

	/// <summary>
	/// Gets or sets the owning checkpoint.
	/// </summary>
	[ForeignKey(nameof(CheckpointId))]
	public Checkpoint? Checkpoint { get; set; }

	/// <summary>
	/// Gets the owner's identifier, whichever kind of owner it is.
	/// </summary>
	[NotMapped]
	public string OwnerId => IncentiveId ?? DirectiveId ?? CheckpointId ?? string.Empty;

	/// <summary>
	/// Determines whether an entity can own schedules: an incentive, a directive or a checkpoint.
	/// </summary>
	public static bool CanOwn(object entity)
		=> entity is Pleiades.Orchestration.Incentive or Pleiades.Orchestration.Directive or Pleiades.Orchestration.Checkpoint;

	/// <summary>
	/// Builds a schedule for <paramref name="owner"/>, naming it through the foreign key of its kind.
	/// </summary>
	/// <exception cref="ArgumentException"><paramref name="owner"/> is not an incentive, a directive or a checkpoint.</exception>
	public static BacklogSchedule For(object owner, BacklogScheduleType type, LifecycleState state, DateTime time)
	{
		ArgumentNullException.ThrowIfNull(owner);
		var schedule = new BacklogSchedule { Type = type, State = state, Time = time };
		switch (owner)
		{
			case Incentive incentive:
				schedule.IncentiveId = incentive.Id;
				break;
			case Directive directive:
				schedule.DirectiveId = directive.Id;
				break;
			case Checkpoint checkpoint:
				schedule.CheckpointId = checkpoint.Id;
				break;
			default:
				throw new ArgumentException($"A {owner.GetType().Name} cannot own a backlog schedule; only incentives, directives and checkpoints can.", nameof(owner));
		}

		return schedule;
	}

	/// <summary>
	/// Determines whether this schedule and <paramref name="other"/> occupy the same slot: the same owner, type and
	/// state.
	/// </summary>
	public bool SharesSlotWith(BacklogSchedule other)
	{
		ArgumentNullException.ThrowIfNull(other);
		return Type == other.Type
			&& State == other.State
			&& string.Equals(IncentiveId, other.IncentiveId, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(DirectiveId, other.DirectiveId, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(CheckpointId, other.CheckpointId, StringComparison.OrdinalIgnoreCase);
	}
}
