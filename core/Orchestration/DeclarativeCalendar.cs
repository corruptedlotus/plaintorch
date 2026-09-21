namespace Pleiades.Orchestration;

/// <summary>
/// Selects the calendar a declarative's Orbit resolves against (PEP100). Only month/week/year boundaries
/// differ between calendars; resolved occurrence instants are calendar-agnostic civil datetimes, so this is
/// a resolution concern only.
/// </summary>
public enum DeclarativeCalendar
{
	/// <summary>The Gregorian calendar.</summary>
	Gregorian,

	/// <summary>The six-month Pleiadean calendar.</summary>
	Pleiadean,
}
