namespace Pleiades.Orchestration;

/// <summary>
/// Identifies the kind of entity at one end of a <see cref="Dependency"/> edge (PEP101). Endpoints are
/// heterogeneous and loosely referenced (no EF foreign key), so the kind disambiguates how the reference is
/// resolved. Lunar directives are excluded from the dependency system.
/// </summary>
public enum DependencyEndpointKind
{
	/// <summary>
	/// A stellar directive, referenced by its PUCK id.
	/// </summary>
	Directive,

	/// <summary>
	/// An objective, referenced by its PUCK id.
	/// </summary>
	Objective,

	/// <summary>
	/// A whole fate (all of its occurrences), referenced by its PUCK id.
	/// </summary>
	Fate,

	/// <summary>
	/// A single occurrence of a fate or objective, referenced by the owner PUCK id (iCalendar <c>UID</c>) plus
	/// its original occurrence slot (iCalendar <c>RECURRENCE-ID</c>).
	/// </summary>
	Eventive,

	/// <summary>
	/// A checkpoint, referenced by its PUCK id.
	/// </summary>
	Checkpoint,
}
