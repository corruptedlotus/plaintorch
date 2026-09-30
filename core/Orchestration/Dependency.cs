using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents a directed blocking edge: the <em>source</em> (prerequisite) blocks the <em>target</em>
/// (dependant) until satisfied (PEP101). Endpoints are heterogeneous and loosely referenced (no EF foreign
/// key), so referential integrity is enforced in the application layer, like the incentive parent system.
/// </summary>
/// <remarks>
/// The <see cref="Trigger"/> (source side) selects which source lifecycle event satisfies the dependency; the
/// <see cref="Constraint"/> (target side) selects which target transition is gated. Both default when omitted
/// (finish-triggered, begin-constraining) and are empty when the respective endpoint is a checkpoint.
/// An <see cref="DependencyOrigin.Intrinsic"/> edge is the same shape derived from the backlog's structure rather
/// than stored (PEP102 patch 9): it carries no <see cref="Id"/> and its <see cref="Satisfied"/> is computed as it is
/// derived.
/// </remarks>
public sealed class Dependency
{
	[Key]
	/// <summary>
	/// Gets or sets the database identity for the dependency edge; <c>0</c> for an intrinsic edge, which has none.
	/// </summary>
	public long Id { get; set; }

	/// <summary>
	/// Gets or sets where the edge comes from: stored and drawn by a person, or derived from the backlog's structure.
	/// Not a column — a stored row is explicit by definition, and an intrinsic edge is never stored.
	/// </summary>
	[NotMapped]
	public DependencyOrigin Origin { get; set; }

	/// <summary>
	/// Gets or sets the source (blocking/prerequisite) endpoint kind.
	/// </summary>
	public DependencyEndpointKind SourceKind { get; set; }

	/// <summary>
	/// Gets or sets the source endpoint id (PUCK id, or eventive owner id acting as the iCalendar <c>UID</c>).
	/// </summary>
	public required string SourceId { get; set; }

	/// <summary>
	/// Gets or sets the source occurrence slot (iCalendar <c>RECURRENCE-ID</c>, the occurrence's original moment)
	/// for an eventive source; <see langword="null"/> for a whole-entity or non-eventive source.
	/// </summary>
	public DateTime? SourceRecurrenceId { get; set; }

	/// <summary>
	/// Gets or sets the target (blocked/dependant) endpoint kind.
	/// </summary>
	public DependencyEndpointKind TargetKind { get; set; }

	/// <summary>
	/// Gets or sets the target endpoint id (PUCK id, or eventive owner id acting as the iCalendar <c>UID</c>).
	/// </summary>
	public required string TargetId { get; set; }

	/// <summary>
	/// Gets or sets the target occurrence slot (iCalendar <c>RECURRENCE-ID</c>, the occurrence's original moment)
	/// for an eventive target; <see langword="null"/> for a whole-entity or non-eventive target.
	/// </summary>
	public DateTime? TargetRecurrenceId { get; set; }

	/// <summary>
	/// Gets or sets the trigger on the source side. <see langword="null"/> resolves to the default
	/// (<see cref="DependencyTrigger.OnFinish"/>) for non-checkpoint sources, and stays empty for checkpoints.
	/// </summary>
	public DependencyTrigger? Trigger { get; set; }

	/// <summary>
	/// Gets or sets the constraint on the target side. <see langword="null"/> resolves to the default
	/// (<see cref="DependencyConstraint.ToBegin"/>) for non-checkpoint targets, and stays empty for checkpoints.
	/// </summary>
	public DependencyConstraint? Constraint { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the dependency is currently satisfied. Maintained by the
	/// dependency reconciler.
	/// </summary>
	public bool Satisfied { get; set; }

	/// <summary>
	/// Gets or sets whether this dependency, while unsatisfied, gates the next lifecycle transition its target has to
	/// make: its begin while it has not begun, its finish once it has begun and not yet finished, or a checkpoint's
	/// unlock. <see langword="false"/> once satisfied, and for a dependency whose constraint concerns a transition the
	/// target has already made or is not yet up to. Computed when the edge is served
	/// (<c>DependencyGateService.StampNextTransitionAsync</c>), never stored.
	/// </summary>
	[NotMapped]
	public bool GatesNextTransition { get; set; }

	[NotMapped]
	/// <summary>
	/// Gets the source endpoint as a reference value.
	/// </summary>
	public EndpointRef Source => new(SourceKind, SourceId, SourceRecurrenceId);

	[NotMapped]
	/// <summary>
	/// Gets the target endpoint as a reference value.
	/// </summary>
	public EndpointRef Target => new(TargetKind, TargetId, TargetRecurrenceId);

	/// <summary>
	/// Derives an intrinsic edge (PEP102 patch 9): the source's finish blocks the target's finish. The trigger and
	/// constraint are written out rather than left to default, so the edge reads the same wherever it is served.
	/// </summary>
	/// <param name="source">The prerequisite endpoint (a child of the target).</param>
	/// <param name="target">The dependant endpoint (the parent).</param>
	/// <param name="satisfied">Whether the source has finished, computed with the edge.</param>
	public static Dependency Intrinsic(EndpointRef source, EndpointRef target, bool satisfied)
	{
		return new Dependency
		{
			Origin = DependencyOrigin.Intrinsic,
			SourceKind = source.Kind,
			SourceId = source.Id,
			SourceRecurrenceId = source.RecurrenceId,
			TargetKind = target.Kind,
			TargetId = target.Id,
			TargetRecurrenceId = target.RecurrenceId,
			Trigger = DependencyTrigger.OnFinish,
			Constraint = DependencyConstraint.ToFinish,
			Satisfied = satisfied,
		};
	}
}
