using Pleiades.Orchestration;

namespace Pleiades.Plaintorch.Materialization;

/// <summary>
/// Window geometry shared by materialization and Polaris inclusion listing: resolving a cycle's 24h inclusion
/// window, enumerating its candidate dates, and deciding whether a dated occurrence or an attentive collides
/// with a window.
/// </summary>
public static class InclusionWindow
{
	/// <summary>
	/// Resolves the 24h inclusion window that follows a cycle's starting point.
	/// </summary>
	public static (DateTime Start, DateTime End) Resolve(PolarisCycle cycle)
	{
		ArgumentNullException.ThrowIfNull(cycle);
		var start = cycle.StartTime!.Value.LocalDateTime;
		return (start, start.AddHours(24));
	}

	/// <summary>
	/// Enumerates the occurrence dates that can possibly intersect the window (used to prefilter in SQL).
	/// </summary>
	public static List<DateOnly> EnumerateDates(DateTime windowStart, DateTime windowEnd)
	{
		var dates = new List<DateOnly>();
		for (var date = DateOnly.FromDateTime(windowStart); date <= DateOnly.FromDateTime(windowEnd); date = date.AddDays(1))
		{
			dates.Add(date);
		}

		return dates;
	}

	/// <summary>
	/// Determines whether an occurrence intersects the inclusion window. Items without times occupy their
	/// whole day; timed items occupy their start/end span.
	/// </summary>
	public static bool Intersects(DateOnly date, TimeOnly? startTime, TimeOnly? endTime, DateTime windowStart, DateTime windowEnd)
	{
		var occurrenceStart = startTime is null
			? date.ToDateTime(TimeOnly.MinValue)
			: date.ToDateTime(startTime.Value);
		var occurrenceEnd = startTime is null
			? date.AddDays(1).ToDateTime(TimeOnly.MinValue)
			: date.ToDateTime(endTime ?? startTime.Value);

		return occurrenceEnd >= windowStart && occurrenceStart < windowEnd;
	}

	/// <summary>
	/// Determines whether an attentive collides with the inclusion window: an attentive with a duration — a
	/// super-day period or a span — occupies [moment, moment + duration), a timed one its instant, and an all-day
	/// one its whole day.
	/// </summary>
	public static bool AttentiveIntersects(Attentive attentive, DateTime windowStart, DateTime windowEnd)
	{
		ArgumentNullException.ThrowIfNull(attentive);
		if (attentive.Epoch.Duration is not null)
		{
			return attentive.Epoch.EndMoment >= windowStart && attentive.Epoch.Moment < windowEnd;
		}

		return Intersects(attentive.Epoch.Date, attentive.Epoch.TimeOfDay, attentive.Epoch.TimeOfDay, windowStart, windowEnd);
	}

	/// <summary>
	/// Determines whether an eventive collides with the inclusion window, using its epoch: the occurrence spans
	/// [moment, moment + duration) — an all-day occurrence's duration being its whole day.
	/// </summary>
	public static bool EventiveIntersects(Eventive eventive, DateTime windowStart, DateTime windowEnd)
	{
		ArgumentNullException.ThrowIfNull(eventive);
		return eventive.Epoch.EndMoment >= windowStart && eventive.Epoch.Moment < windowEnd;
	}
}
