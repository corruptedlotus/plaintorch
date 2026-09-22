using Pleiades.Orbits;

namespace Pleiades.Orchestration;

/// <summary>
/// An occurrence's position in time (PEP100/PEP111): a wall-clock <see cref="Moment"/> plus how much of it is
/// real (<see cref="Granularity"/>) and how long the real block lasts (<see cref="Duration"/>). This is the
/// shape the Orbit engine emits — a moment and the unit window it stands for — stored directly so an occurrence
/// maps 1:1 onto an iCalendar instance.
/// </summary>
/// <remarks>
/// Owned by an occurrence (EF <c>OwnsOne</c>), so its fields live as columns on the occurrence's own table.
/// <para>
/// Floating is implicit: when the <see cref="Granularity"/> window is larger than <see cref="Duration"/> (e.g.
/// day granularity with a 2h duration) the block floats within the window; a null <see cref="Duration"/> fills
/// exactly one granularity unit, and a duration at or beyond the window is anchored at <see cref="Moment"/>.
/// </para>
/// </remarks>
public sealed class Epoch
{
	/// <summary>
	/// Gets or sets the occurrence's civil (wall-clock) moment. Interpreted in <see cref="TimeZone"/>, or as
	/// floating local time when that is <see langword="null"/>; never an absolute UTC instant.
	/// </summary>
	public DateTime Moment { get; set; }

	/// <summary>
	/// Gets or sets how much of <see cref="Moment"/> is meaningful — the unit window the occurrence stands for
	/// (its accuracy): a day-granular occurrence occupies a whole day, an hour-granular one an hour, and so on.
	/// </summary>
	public OrbitUnit Granularity { get; set; }

	/// <summary>
	/// Gets or sets the nominal length of the real block, in Orbit duration notation (<c>"5h"</c>, <c>"1d6h"</c>,
	/// <c>"1M"</c>). <see langword="null"/> means the block fills exactly one <see cref="Granularity"/> unit.
	/// Nominal (calendar-relative), not a fixed number of seconds.
	/// </summary>
	public string? Duration { get; set; }

	/// <summary>
	/// Gets or sets the time zone <see cref="Moment"/> is anchored in. <see langword="null"/> means wall/floating
	/// time (no zone).
	/// </summary>
	public string? TimeZone { get; set; }
}
