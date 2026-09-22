namespace Pleiades.Orchestration;

/// <summary>
/// A resolved reference to one end of a <see cref="Dependency"/> (PEP101): a kind plus an id (the entity's
/// PUCK id, or an eventive owner's PUCK id acting as the iCalendar <c>UID</c>), optionally qualified by a single
/// occurrence slot (<see cref="Recurrence"/>, the iCalendar <c>RECURRENCE-ID</c>) for one eventive occurrence.
/// </summary>
/// <param name="Kind">The endpoint kind.</param>
/// <param name="Id">The PUCK id (or eventive owner id / <c>UID</c>).</param>
/// <param name="Recurrence">The occurrence slot (date, optional time) for an eventive endpoint; <see langword="null"/>
/// for a whole-entity or non-eventive endpoint.</param>
public readonly record struct EndpointRef(
	DependencyEndpointKind Kind,
	string Id,
	RecurrenceId? Recurrence = null);
