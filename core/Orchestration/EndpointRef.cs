namespace Pleiades.Orchestration;

/// <summary>
/// A resolved reference to one end of a <see cref="Dependency"/> (PEP101): a kind plus an id (the entity's
/// PUCK id, or an eventive owner's PUCK id acting as the iCalendar <c>UID</c>), optionally qualified by an
/// original occurrence slot (the iCalendar <c>RECURRENCE-ID</c>) for a single eventive occurrence.
/// </summary>
/// <param name="Kind">The endpoint kind.</param>
/// <param name="Id">The PUCK id (or eventive owner id / <c>UID</c>).</param>
/// <param name="RecurrenceDate">The occurrence slot date for an eventive endpoint; otherwise <see langword="null"/>.</param>
/// <param name="RecurrenceTime">The occurrence slot time for a timed eventive; <see langword="null"/> for all-day or non-eventive endpoints.</param>
public readonly record struct EndpointRef(
	DependencyEndpointKind Kind,
	string Id,
	DateOnly? RecurrenceDate = null,
	TimeOnly? RecurrenceTime = null);
