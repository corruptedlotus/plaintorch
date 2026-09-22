using Pleiades.Calendar;

namespace Pleiades.Orbits;

/// <summary>
/// The Pleiadean calendar system for the orbit engine: six months per year, months 1–5 of 61 days and a
/// sixth month of 60/61 days (leap-coupled to the Persian calendar, matching
/// <see cref="PleiadeanCalendar"/>). Weeks are seven-day cycles anchored on <b>Saturday</b> (Saturday = day 1
/// of the week, Friday = day 7) — unlike the Gregorian resolver's Monday-anchored week; hours, minutes, and
/// seconds are unchanged. This is the default resolver calendar for decree orbits (attentives) and for
/// reflective day-matching (PEP100); fate orbits (eventives) keep the Gregorian calendar.
/// </summary>
public sealed class OrbitPleiadeanCalendar : IOrbitCalendar
{
	// Days since the Unix epoch of Pleiadean year 0, month 1, day 1 (Persian 1402-01-01).
	private static readonly long EpochDays = ComputeEpochDays();

	private static long ComputeEpochDays()
	{
		var persian = new System.Globalization.PersianCalendar();
		var epoch = persian.ToDateTime(1402, 1, 1, 0, 0, 0, 0);
		return JsDate.DaysFromCivil(epoch.Year, epoch.Month, epoch.Day);
	}

	/// <summary>Resolves the Pleiadean civil date for an instant.</summary>
	private static (int Year, int Month, int Day) PleiadeanFromDays(long unixDays)
	{
		var days = unixDays - EpochDays;
		var year = 0;
		if (days >= 0)
		{
			while (days >= PleiadeanCalendar.GetDaysInYear(year))
			{
				days -= PleiadeanCalendar.GetDaysInYear(year);
				year++;
			}
		}
		else
		{
			while (days < 0)
			{
				year--;
				days += PleiadeanCalendar.GetDaysInYear(year);
			}
		}

		var month = 1;
		while (month <= 6 && days >= PleiadeanCalendar.GetDaysInMonth(year, month))
		{
			days -= PleiadeanCalendar.GetDaysInMonth(year, month);
			month++;
		}

		return (year, month, (int)days + 1);
	}

	/// <summary>Resolves the unix day of a Pleiadean civil date, normalizing month and day overflow.</summary>
	private static long DaysFromPleiadean(int year, int month, int day)
	{
		// Normalize the month into the year (six months per Pleiadean year).
		year += (int)JsDate.FloorDiv(month - 1, 6);
		month = (int)JsDate.FloorMod(month - 1, 6) + 1;

		long days = 0;
		if (year >= 0)
		{
			for (var y = 0; y < year; y++)
			{
				days += PleiadeanCalendar.GetDaysInYear(y);
			}
		}
		else
		{
			for (var y = -1; y >= year; y--)
			{
				days -= PleiadeanCalendar.GetDaysInYear(y);
			}
		}

		for (var m = 1; m < month; m++)
		{
			days += PleiadeanCalendar.GetDaysInMonth(year, m);
		}

		// Day overflow/underflow rolls by plain day arithmetic, mirroring the JS Date semantics
		// the Gregorian orbit calendar replicates.
		return EpochDays + days + (day - 1);
	}

	/// <summary>
	/// The Pleiadean day of the week for an instant: <b>Saturday = 1 … Friday = 7</b>. Remaps
	/// <see cref="JsDate.UtcDayOfWeek"/> (JS convention, 0 = Sunday … 6 = Saturday) onto a Saturday-first cycle.
	/// </summary>
	private static int PleiadeanDayOfWeek(long ms) => (JsDate.UtcDayOfWeek(ms) + 1) % 7 + 1;

	public int Get(long ms, OrbitUnit unit, OrbitUnit? parent = null)
	{
		switch (unit)
		{
			case OrbitUnit.Year: return PleiadeanFromDays(JsDate.EpochDays(ms)).Year;
			case OrbitUnit.Month: return PleiadeanFromDays(JsDate.EpochDays(ms)).Month;
			case OrbitUnit.Week:
			{
				var (year, month, day) = PleiadeanFromDays(JsDate.EpochDays(ms));
				var firstDayMs = DaysFromPleiadean(year, month, 1) * JsDate.MsPerDay;
				// Week-of-month rows break on the Saturday-anchored week, so the month's first day is placed by
				// its Saturday-first index.
				var firstDayOfWeek = PleiadeanDayOfWeek(firstDayMs);
				return (day + firstDayOfWeek - 1 + 6) / 7; // ceil
			}
			case OrbitUnit.Day:
				if (parent == OrbitUnit.Week)
				{
					return PleiadeanDayOfWeek(ms); // 1 = Saturday … 7 = Friday
				}

				return PleiadeanFromDays(JsDate.EpochDays(ms)).Day;
			case OrbitUnit.Hour: return JsDate.UtcHours(ms);
			case OrbitUnit.Minute: return JsDate.UtcMinutes(ms);
			case OrbitUnit.Second: return JsDate.UtcSeconds(ms);
			default: throw new ArgumentOutOfRangeException(nameof(unit));
		}
	}

	public long Set(long ms, OrbitUnit unit, int value)
	{
		var timeOfDay = JsDate.TimeOfDayMs(ms);
		switch (unit)
		{
			case OrbitUnit.Year:
			{
				var (_, month, day) = PleiadeanFromDays(JsDate.EpochDays(ms));
				return DaysFromPleiadean(value, month, day) * JsDate.MsPerDay + timeOfDay;
			}
			case OrbitUnit.Month:
			{
				var (year, _, day) = PleiadeanFromDays(JsDate.EpochDays(ms));
				return DaysFromPleiadean(year, value, day) * JsDate.MsPerDay + timeOfDay;
			}
			case OrbitUnit.Week:
			{
				var currentWeek = Get(ms, OrbitUnit.Week);
				return ms + (long)(value - currentWeek) * JsDate.MsPerWeek;
			}
			case OrbitUnit.Day:
			{
				var (year, month, _) = PleiadeanFromDays(JsDate.EpochDays(ms));
				return DaysFromPleiadean(year, month, value) * JsDate.MsPerDay + timeOfDay;
			}
			case OrbitUnit.Hour: return JsDate.SetUtcHours(ms, value);
			case OrbitUnit.Minute: return JsDate.SetUtcMinutes(ms, value);
			case OrbitUnit.Second: return JsDate.SetUtcSeconds(ms, value);
			default: throw new ArgumentOutOfRangeException(nameof(unit));
		}
	}

	public long Add(long ms, OrbitUnit unit, int amount)
	{
		switch (unit)
		{
			case OrbitUnit.Year:
			{
				var (year, month, day) = PleiadeanFromDays(JsDate.EpochDays(ms));
				return DaysFromPleiadean(year + amount, month, day) * JsDate.MsPerDay + JsDate.TimeOfDayMs(ms);
			}
			case OrbitUnit.Month:
			{
				var (year, month, day) = PleiadeanFromDays(JsDate.EpochDays(ms));
				return DaysFromPleiadean(year, month + amount, day) * JsDate.MsPerDay + JsDate.TimeOfDayMs(ms);
			}
			case OrbitUnit.Week: return ms + (long)amount * JsDate.MsPerWeek;
			case OrbitUnit.Day: return ms + (long)amount * JsDate.MsPerDay;
			case OrbitUnit.Hour: return ms + (long)amount * JsDate.MsPerHour;
			case OrbitUnit.Minute: return ms + (long)amount * JsDate.MsPerMinute;
			case OrbitUnit.Second: return ms + (long)amount * JsDate.MsPerSecond;
			default: throw new ArgumentOutOfRangeException(nameof(unit));
		}
	}

	public int Min(OrbitUnit unit, long contextMs)
	{
		return unit is OrbitUnit.Hour or OrbitUnit.Minute or OrbitUnit.Second ? 0 : 1;
	}

	public int Max(OrbitUnit unit, long contextMs)
	{
		switch (unit)
		{
			case OrbitUnit.Year: return 9999;
			case OrbitUnit.Month: return 6;
			// Pleiadean months span 60–61 days, so a month can straddle up to ten week rows.
			case OrbitUnit.Week: return 10;
			case OrbitUnit.Day:
			{
				var (year, month, _) = PleiadeanFromDays(JsDate.EpochDays(contextMs));
				return PleiadeanCalendar.GetDaysInMonth(year, month);
			}
			case OrbitUnit.Hour: return 23;
			case OrbitUnit.Minute: return 59;
			case OrbitUnit.Second: return 59;
			default: throw new ArgumentOutOfRangeException(nameof(unit));
		}
	}

	public long SnapToStart(long ms, OrbitUnit unit)
	{
		switch (unit)
		{
			case OrbitUnit.Year:
			{
				var (year, _, _) = PleiadeanFromDays(JsDate.EpochDays(ms));
				return DaysFromPleiadean(year, 1, 1) * JsDate.MsPerDay;
			}
			case OrbitUnit.Month:
			{
				var (year, month, _) = PleiadeanFromDays(JsDate.EpochDays(ms));
				return DaysFromPleiadean(year, month, 1) * JsDate.MsPerDay;
			}
			case OrbitUnit.Week:
			{
				var currentDayOfWeek = Get(ms, OrbitUnit.Day, OrbitUnit.Week);
				return (JsDate.EpochDays(ms) - (currentDayOfWeek - 1)) * JsDate.MsPerDay;
			}
			case OrbitUnit.Day: return JsDate.EpochDays(ms) * JsDate.MsPerDay;
			case OrbitUnit.Hour: return ms - JsDate.TimeOfDayMs(ms) % JsDate.MsPerHour;
			case OrbitUnit.Minute: return ms - JsDate.TimeOfDayMs(ms) % JsDate.MsPerMinute;
			case OrbitUnit.Second: return ms - JsDate.TimeOfDayMs(ms) % JsDate.MsPerSecond;
			default: throw new ArgumentOutOfRangeException(nameof(unit));
		}
	}

	public long Delta(long ms1, long ms2, OrbitUnit unit)
	{
		var sign = ms1 >= ms2 ? 1 : -1;
		var start = ms1 >= ms2 ? ms2 : ms1;
		var end = ms1 >= ms2 ? ms1 : ms2;

		switch (unit)
		{
			case OrbitUnit.Year:
			{
				var s = PleiadeanFromDays(JsDate.EpochDays(start));
				var e = PleiadeanFromDays(JsDate.EpochDays(end));
				return sign * (long)(e.Year - s.Year);
			}
			case OrbitUnit.Month:
			{
				var s = PleiadeanFromDays(JsDate.EpochDays(start));
				var e = PleiadeanFromDays(JsDate.EpochDays(end));
				return sign * ((long)(e.Year - s.Year) * 6 + (e.Month - s.Month));
			}
			case OrbitUnit.Week:
				return sign * ((SnapToStart(end, OrbitUnit.Week) - SnapToStart(start, OrbitUnit.Week)) / JsDate.MsPerWeek);
			case OrbitUnit.Day:
				return sign * ((SnapToStart(end, OrbitUnit.Day) - SnapToStart(start, OrbitUnit.Day)) / JsDate.MsPerDay);
			case OrbitUnit.Hour:
				return sign * ((SnapToStart(end, OrbitUnit.Hour) - SnapToStart(start, OrbitUnit.Hour)) / JsDate.MsPerHour);
			case OrbitUnit.Minute:
				return sign * ((SnapToStart(end, OrbitUnit.Minute) - SnapToStart(start, OrbitUnit.Minute)) / JsDate.MsPerMinute);
			case OrbitUnit.Second:
				return sign * ((SnapToStart(end, OrbitUnit.Second) - SnapToStart(start, OrbitUnit.Second)) / JsDate.MsPerSecond);
			default:
				throw new ArgumentOutOfRangeException(nameof(unit));
		}
	}
}
