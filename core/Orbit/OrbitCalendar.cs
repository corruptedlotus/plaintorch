namespace Pleiades.Orbits;

// C# port of @pleiades/orbits calendar.ts. All instants are milliseconds since the Unix
// epoch (UTC), and the mutation helpers replicate JavaScript Date UTC-setter semantics
// exactly — including day-of-month overflow rolling into subsequent months — so the two
// engines resolve identical streams.

/// <summary>
/// The calendar arithmetic surface the orbit engine resolves against.
/// </summary>
public interface IOrbitCalendar
{
	int Get(long ms, OrbitUnit unit, OrbitUnit? parent = null);
	long Set(long ms, OrbitUnit unit, int value);
	long Add(long ms, OrbitUnit unit, int amount);
	int Min(OrbitUnit unit, long contextMs);
	int Max(OrbitUnit unit, long contextMs);
	long Delta(long ms1, long ms2, OrbitUnit unit);
	long SnapToStart(long ms, OrbitUnit unit);
}

/// <summary>
/// Exact-JavaScript-semantics UTC date arithmetic over epoch milliseconds.
/// </summary>
public static class JsDate
{
	public const long MsPerSecond = 1_000;
	public const long MsPerMinute = 60_000;
	public const long MsPerHour = 3_600_000;
	public const long MsPerDay = 86_400_000;
	public const long MsPerWeek = 604_800_000;

	public static long FloorDiv(long value, long divisor)
	{
		var quotient = value / divisor;
		return value % divisor < 0 ? quotient - 1 : quotient;
	}

	public static long FloorMod(long value, long divisor)
	{
		var remainder = value % divisor;
		return remainder < 0 ? remainder + divisor : remainder;
	}

	/// <summary>Days since the epoch (1970-01-01), floored.</summary>
	public static long EpochDays(long ms) => FloorDiv(ms, MsPerDay);

	/// <summary>The time-of-day remainder in milliseconds.</summary>
	public static long TimeOfDayMs(long ms) => FloorMod(ms, MsPerDay);

	/// <summary>Civil date from days since epoch (Howard Hinnant's algorithm).</summary>
	public static (int Year, int Month, int Day) CivilFromDays(long days)
	{
		var z = days + 719_468;
		var era = FloorDiv(z, 146_097);
		var doe = z - era * 146_097;                                   // [0, 146096]
		var yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365; // [0, 399]
		var y = yoe + era * 400;
		var doy = doe - (365 * yoe + yoe / 4 - yoe / 100);             // [0, 365]
		var mp = (5 * doy + 2) / 153;                                  // [0, 11]
		var d = doy - (153 * mp + 2) / 5 + 1;                          // [1, 31]
		var m = mp < 10 ? mp + 3 : mp - 9;                             // [1, 12]
		return ((int)(y + (m <= 2 ? 1 : 0)), (int)m, (int)d);
	}

	/// <summary>Days since epoch from a civil date (Howard Hinnant's algorithm).</summary>
	public static long DaysFromCivil(int year, int month, int day)
	{
		var y = (long)year - (month <= 2 ? 1 : 0);
		var era = FloorDiv(y, 400);
		var yoe = y - era * 400;                                       // [0, 399]
		var doy = (153L * (month > 2 ? month - 3 : month + 9) + 2) / 5 + day - 1; // [0, 365]
		var doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;               // [0, 146096]
		return era * 146_097 + doe - 719_468;
	}

	/// <summary>JS <c>getUTCDay()</c>: 0 = Sunday ... 6 = Saturday.</summary>
	public static int UtcDayOfWeek(long ms)
	{
		// 1970-01-01 was a Thursday (4).
		return (int)FloorMod(EpochDays(ms) + 4, 7);
	}

	public static int UtcFullYear(long ms) => CivilFromDays(EpochDays(ms)).Year;
	public static int UtcMonth(long ms) => CivilFromDays(EpochDays(ms)).Month; // 1-based here
	public static int UtcDate(long ms) => CivilFromDays(EpochDays(ms)).Day;
	public static int UtcHours(long ms) => (int)(TimeOfDayMs(ms) / MsPerHour);
	public static int UtcMinutes(long ms) => (int)(TimeOfDayMs(ms) % MsPerHour / MsPerMinute);
	public static int UtcSeconds(long ms) => (int)(TimeOfDayMs(ms) % MsPerMinute / MsPerSecond);

	/// <summary>Days in the given civil month.</summary>
	public static int DaysInMonth(int year, int month)
	{
		return (int)(DaysFromCivil(month == 12 ? year + 1 : year, month == 12 ? 1 : month + 1, 1) - DaysFromCivil(year, month, 1));
	}

	/// <summary>
	/// Rebuilds an instant from civil components with JS overflow semantics: the month is
	/// normalized into the year, and the day (which may exceed the target month's length,
	/// or be zero/negative) rolls through adjacent months by plain day arithmetic.
	/// </summary>
	public static long FromCivilWithOverflow(int year, int month, int day, long timeOfDayMs)
	{
		var normalizedYear = year + (int)FloorDiv(month - 1, 12);
		var normalizedMonth = (int)FloorMod(month - 1, 12) + 1;
		return (DaysFromCivil(normalizedYear, normalizedMonth, 1) + (day - 1)) * MsPerDay + timeOfDayMs;
	}

	/// <summary>JS <c>setUTCFullYear(value)</c> (keeps month/day, day overflow rolls).</summary>
	public static long SetUtcFullYear(long ms, int value)
	{
		var (_, month, day) = CivilFromDays(EpochDays(ms));
		return FromCivilWithOverflow(value, month, day, TimeOfDayMs(ms));
	}

	/// <summary>JS <c>setUTCMonth(value)</c> with a 1-based month (engine convention).</summary>
	public static long SetUtcMonth1(long ms, int value)
	{
		var (year, _, day) = CivilFromDays(EpochDays(ms));
		return FromCivilWithOverflow(year, value, day, TimeOfDayMs(ms));
	}

	/// <summary>JS <c>setUTCDate(value)</c> (day overflow/underflow rolls through months).</summary>
	public static long SetUtcDate(long ms, int value)
	{
		var (year, month, _) = CivilFromDays(EpochDays(ms));
		return FromCivilWithOverflow(year, month, value, TimeOfDayMs(ms));
	}

	public static long SetUtcHours(long ms, int value)
	{
		return EpochDays(ms) * MsPerDay + value * MsPerHour + TimeOfDayMs(ms) % MsPerHour;
	}

	public static long SetUtcMinutes(long ms, int value)
	{
		return ms - UtcMinutes(ms) * MsPerMinute + value * MsPerMinute;
	}

	public static long SetUtcSeconds(long ms, int value)
	{
		return ms - UtcSeconds(ms) * MsPerSecond + value * MsPerSecond;
	}

	/// <summary>Formats as a JS <c>toISOString()</c>-compatible string (millisecond precision, Z).</summary>
	public static string ToIsoString(long ms)
	{
		var (year, month, day) = CivilFromDays(EpochDays(ms));
		var tod = TimeOfDayMs(ms);
		var hours = tod / MsPerHour;
		var minutes = tod % MsPerHour / MsPerMinute;
		var seconds = tod % MsPerMinute / MsPerSecond;
		var millis = tod % MsPerSecond;
		return string.Create(System.Globalization.CultureInfo.InvariantCulture,
			$"{year:D4}-{month:D2}-{day:D2}T{hours:D2}:{minutes:D2}:{seconds:D2}.{millis:D3}Z");
	}

	/// <summary>Parses an ISO-8601 instant into epoch milliseconds (JS <c>new Date(iso)</c> for valid input).</summary>
	public static bool TryParseIso(string value, out long ms)
	{
		if (System.DateTimeOffset.TryParse(
			value,
			System.Globalization.CultureInfo.InvariantCulture,
			System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
			out var parsed))
		{
			ms = parsed.ToUnixTimeMilliseconds();
			return true;
		}

		ms = 0;
		return false;
	}
}

/// <summary>
/// The Gregorian calendar system, mirroring the TypeScript <c>GregorianCalendar</c>.
/// </summary>
public sealed class OrbitGregorianCalendar : IOrbitCalendar
{
	public int Get(long ms, OrbitUnit unit, OrbitUnit? parent = null)
	{
		switch (unit)
		{
			case OrbitUnit.Year: return JsDate.UtcFullYear(ms);
			case OrbitUnit.Month: return JsDate.UtcMonth(ms);
			case OrbitUnit.Week:
			{
				var (year, month, _) = JsDate.CivilFromDays(JsDate.EpochDays(ms));
				var firstDayMs = JsDate.DaysFromCivil(year, month, 1) * JsDate.MsPerDay;
				var firstDayOfWeek = JsDate.UtcDayOfWeek(firstDayMs);
				if (firstDayOfWeek == 0)
				{
					firstDayOfWeek = 7;
				}

				return (JsDate.UtcDate(ms) + firstDayOfWeek - 1 + 6) / 7; // ceil
			}
			case OrbitUnit.Day:
				if (parent == OrbitUnit.Week)
				{
					var dow = JsDate.UtcDayOfWeek(ms);
					return dow == 0 ? 7 : dow; // 1 = Monday, 7 = Sunday
				}

				return JsDate.UtcDate(ms);
			case OrbitUnit.Hour: return JsDate.UtcHours(ms);
			case OrbitUnit.Minute: return JsDate.UtcMinutes(ms);
			case OrbitUnit.Second: return JsDate.UtcSeconds(ms);
			default: throw new ArgumentOutOfRangeException(nameof(unit));
		}
	}

	public long Set(long ms, OrbitUnit unit, int value)
	{
		switch (unit)
		{
			case OrbitUnit.Year: return JsDate.SetUtcFullYear(ms, value);
			case OrbitUnit.Month: return JsDate.SetUtcMonth1(ms, value);
			case OrbitUnit.Week:
			{
				var currentWeek = Get(ms, OrbitUnit.Week);
				return JsDate.SetUtcDate(ms, JsDate.UtcDate(ms) + (value - currentWeek) * 7);
			}
			case OrbitUnit.Day: return JsDate.SetUtcDate(ms, value);
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
			case OrbitUnit.Year: return JsDate.SetUtcFullYear(ms, JsDate.UtcFullYear(ms) + amount);
			case OrbitUnit.Month: return JsDate.SetUtcMonth1(ms, JsDate.UtcMonth(ms) + amount);
			case OrbitUnit.Week: return ms + amount * JsDate.MsPerWeek;
			case OrbitUnit.Day: return ms + amount * JsDate.MsPerDay;
			case OrbitUnit.Hour: return ms + amount * JsDate.MsPerHour;
			case OrbitUnit.Minute: return ms + amount * JsDate.MsPerMinute;
			case OrbitUnit.Second: return ms + amount * JsDate.MsPerSecond;
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
			case OrbitUnit.Month: return 12;
			case OrbitUnit.Week: return 6; // Accounts safely for partial spillover weeks
			case OrbitUnit.Day:
			{
				var (year, month, _) = JsDate.CivilFromDays(JsDate.EpochDays(contextMs));
				return JsDate.DaysInMonth(year, month);
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
				var year = JsDate.UtcFullYear(ms);
				return JsDate.DaysFromCivil(year, 1, 1) * JsDate.MsPerDay;
			}
			case OrbitUnit.Month:
			{
				var (year, month, _) = JsDate.CivilFromDays(JsDate.EpochDays(ms));
				return JsDate.DaysFromCivil(year, month, 1) * JsDate.MsPerDay;
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
				return sign * (long)(JsDate.UtcFullYear(end) - JsDate.UtcFullYear(start));
			case OrbitUnit.Month:
				return sign * ((long)(JsDate.UtcFullYear(end) - JsDate.UtcFullYear(start)) * 12 + (JsDate.UtcMonth(end) - JsDate.UtcMonth(start)));
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
