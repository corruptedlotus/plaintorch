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
/// </remarks>
public sealed class Dependency
{
	[Key]
	/// <summary>
	/// Gets or sets the database identity for the dependency edge.
	/// </summary>
	public long Id { get; set; }

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
}
