namespace Pleiades.Orchestration;

/// <summary>
/// The iCalendar / CalDAV <c>RECURRENCE-ID</c> of a single occurrence: the original slot start, expressed as a
/// DATE (all-day) or a DATE-TIME. Combined with the owning declarative's UID it is the stable,
/// reschedule-independent identity of one occurrence within a recurrence set — moving the occurrence's current
/// start never changes it.
/// </summary>
/// <param name="Date">The original occurrence date.</param>
/// <param name="Time">The original occurrence time of day; <see langword="null"/> for an all-day (DATE) slot.</param>
public readonly record struct RecurrenceId(DateOnly Date, TimeOnly? Time);
