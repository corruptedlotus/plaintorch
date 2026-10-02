using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
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

	/// <summary>Gets the calendar day of <see cref="Moment"/>. In an EF query, filter on <see cref="Moment"/> instead.</summary>
	[NotMapped]
	public DateOnly Date => DateOnly.FromDateTime(Moment);

	/// <summary>Gets whether the occurrence is day-or-coarser (no meaningful time of day).</summary>
	[NotMapped]
	public bool IsAllDay => Granularity is not (OrbitUnit.Hour or OrbitUnit.Minute or OrbitUnit.Second);

	/// <summary>Gets the time of day of <see cref="Moment"/> for a sub-day occurrence; <see langword="null"/> when all-day.</summary>
	[NotMapped]
	public TimeOnly? TimeOfDay => IsAllDay ? null : TimeOnly.FromDateTime(Moment);

	/// <summary>Gets the exclusive end moment: <see cref="Moment"/> plus the <see cref="Duration"/>, or one granularity unit when none.</summary>
	[NotMapped]
	public DateTime EndMoment => OccurrenceDurations.Apply(Moment, Duration, Granularity);

	/// <summary>
	/// Gets the absolute instant a timed (sub-day) epoch anchored in a <see cref="TimeZone"/> names: <see cref="Moment"/>
	/// read on that zone's clock, in UTC. <see langword="null"/> for a floating epoch (no zone, or one this host does
	/// not know) and for a day-or-coarser one, whose date names a day wherever it is read, as an iCalendar date value
	/// carries no zone. Server-computed.
	/// </summary>
	/// <remarks>
	/// A wall time the zone skips (a spring-forward gap) is read with the offset in force before the gap, so it lands
	/// that much later; a wall time the zone repeats (a fall-back hour) is read as its first, earlier occurrence. This is
	/// the iCalendar and Temporal "compatible" disambiguation.
	/// </remarks>
	[NotMapped]
	public DateTimeOffset? Instant => IsAllDay ? null : AnchoredInstant(Moment, TimeZone);

	/// <summary>
	/// Gets the moment on this host's clock: <see cref="Instant"/> in the host's own zone when the epoch is anchored,
	/// else <see cref="Moment"/> itself, as floating time is read on whatever clock reads it. What the agenda, the
	/// Polaris inclusion window and every comparison with "now" or "today" read. Never stored; filter EF queries on
	/// <see cref="Moment"/>, widened by a day, then on this.
	/// </summary>
	[NotMapped, JsonIgnore]
	public DateTime LocalMoment => Instant is { } instant ? TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.Local).DateTime : Moment;

	/// <summary>Gets the calendar day of <see cref="LocalMoment"/>: the day the occurrence falls on, on this host.</summary>
	[NotMapped, JsonIgnore]
	public DateOnly LocalDate => DateOnly.FromDateTime(LocalMoment);

	/// <summary>Gets the time of day of <see cref="LocalMoment"/> for a sub-day occurrence; <see langword="null"/> when all-day.</summary>
	[NotMapped, JsonIgnore]
	public TimeOnly? LocalTimeOfDay => IsAllDay ? null : TimeOnly.FromDateTime(LocalMoment);

	/// <summary>Gets the exclusive end on this host's clock: <see cref="LocalMoment"/> plus the epoch's nominal span.</summary>
	[NotMapped, JsonIgnore]
	public DateTime LocalEndMoment => LocalMoment + (EndMoment - Moment);

	private static DateTimeOffset? AnchoredInstant(DateTime moment, string? timeZone)
	{
		if (OccurrenceZones.Find(timeZone) is not { } zone)
		{
			return null;
		}

		var civil = DateTime.SpecifyKind(moment, DateTimeKind.Unspecified);
		var offset = zone.IsInvalidTime(civil)
			? zone.GetUtcOffset(civil.AddDays(-1))
			: zone.IsAmbiguousTime(civil)
				? zone.GetAmbiguousTimeOffsets(civil).Max()
				: zone.GetUtcOffset(civil);
		return new DateTimeOffset(civil, offset).ToUniversalTime();
	}

	/// <summary>Builds an epoch from a resolved occurrence's day/time-of-day, granularity, and whole-minute length.</summary>
	public static Epoch From(DateOnly date, TimeOnly? timeOfDay, OrbitUnit granularity, int? durationMinutes = null, string? timeZone = null) => new()
	{
		Moment = date.ToDateTime(timeOfDay ?? TimeOnly.MinValue),
		Granularity = granularity,
		Duration = OccurrenceDurations.Format(durationMinutes),
		TimeZone = timeZone,
	};

	/// <summary>
	/// Builds the epoch of a resolved orbit occurrence: its moment and granularity, with its span as the duration —
	/// or, for a super-day occurrence (week/month/year), its whole period in days — anchored in
	/// <paramref name="timeZone"/>, the zone the orbit runs on (its declarative's), or floating when none.
	/// </summary>
	/// <remarks>
	/// The period is resolved on the declarative's own calendar, so storing it as the duration keeps a Pleiadean
	/// month's 60/61-day period exact, where the one-unit fallback of <see cref="EndMoment"/> adds a nominal
	/// (Gregorian) month. A day-or-finer occurrence spans at most its own window and keeps a null duration.
	/// </remarks>
	public static Epoch For(OrbitOccurrenceInstance occurrence, string? timeZone = null)
	{
		ArgumentNullException.ThrowIfNull(occurrence);
		var periodDays = occurrence.PeriodEndExclusive.DayNumber - occurrence.Date.DayNumber;
		return From(
			occurrence.Date,
			occurrence.StartTime,
			occurrence.Granularity,
			occurrence.DurationMinutes ?? (periodDays > 1 ? periodDays * 24 * 60 : null),
			timeZone);
	}

	/// <summary>
	/// Builds the epoch of an occurrence known only by its slot moment — one with no orbit to resolve its shape:
	/// a midnight slot is an all-day (day-granular) occurrence, any other a minute-granular timed one, anchored in
	/// <paramref name="timeZone"/> when given.
	/// </summary>
	public static Epoch FromSlot(DateTime slot, string? timeZone = null) => slot.TimeOfDay == TimeSpan.Zero
		? From(DateOnly.FromDateTime(slot), timeOfDay: null, OrbitUnit.Day, timeZone: timeZone)
		: From(DateOnly.FromDateTime(slot), TimeOnly.FromDateTime(slot), OrbitUnit.Minute, timeZone: timeZone);
}
