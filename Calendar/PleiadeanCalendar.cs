using System.Globalization;

namespace Pleiades.Calendar;

/// <summary>
/// Represents a date in the custom six-month Pleiadean calendar.
/// </summary>
public class PleiadeanCalendar
{
	private static readonly PersianCalendar PersianCalendar = new PersianCalendar();
	private static readonly DateTime Epoch = PersianCalendar.ToDateTime(1402, 1, 1, 0, 0, 0, 0);

	/// <summary>
	/// Gets the Pleiadean year.
	/// </summary>
	public int Year { get; }

	/// <summary>
	/// Gets the Pleiadean month.
	/// </summary>
	public int Month { get; }

	/// <summary>
	/// Gets the Pleiadean day of month.
	/// </summary>
	public int Day { get; }

	/// <summary>
	/// Initializes a Pleiadean calendar date.
	/// </summary>
	public PleiadeanCalendar(int year, int month, int day)
	{
		if (month < 1 || month > 6)
			throw new ArgumentOutOfRangeException(nameof(month), "Month must be between 1 and 6.");
		if (day < 1 || day > GetDaysInMonth(year, month))
			throw new ArgumentOutOfRangeException(nameof(day), $"Day must be between 1 and {GetDaysInMonth(year, month)}.");

		Year = year;
		Month = month;
		Day = day;
	}

	/// <summary>
	/// Converts a Gregorian date into a Pleiadean date.
	/// </summary>
	public static PleiadeanCalendar FromDateTime(DateTime dateTime)
	{
		int daysSinceEpoch = (int)(dateTime.Date - Epoch).TotalDays;
		int year = 0;

		while (true)
		{
			int yearDays = GetDaysInYear(year);
			if (daysSinceEpoch < yearDays)
				break;
			daysSinceEpoch -= yearDays;
			year++;
		}

		int month = 1;
		while (month <= 6)
		{
			int monthDays = GetDaysInMonth(year, month);
			if (daysSinceEpoch < monthDays)
				break;
			daysSinceEpoch -= monthDays;
			month++;
		}

		int day = daysSinceEpoch + 1;
		return new PleiadeanCalendar(year, month, day);
	}

	/// <summary>
	/// Converts this Pleiadean date into a Gregorian date.
	/// </summary>
	public DateTime ToDateTime()
	{
		int days = 0;
		for (int y = 0; y < Year; y++)
			days += GetDaysInYear(y);

		for (int m = 1; m < Month; m++)
			days += GetDaysInMonth(Year, m);

		days += (Day - 1);
		return Epoch.AddDays(days);
	}

	/// <summary>
	/// Gets the number of days in the given Pleiadean year.
	/// </summary>
	public static int GetDaysInYear(int year)
	{
		return 5 * 61 + (IsLeapYear(year) ? 61 : 60);
	}

	/// <summary>
	/// Gets the number of days in a given Pleiadean month.
	/// </summary>
	public static int GetDaysInMonth(int year, int month)
	{
		if (month >= 1 && month <= 5)
			return 61;
		if (month == 6)
			return IsLeapYear(year) ? 61 : 60;
		throw new ArgumentOutOfRangeException(nameof(month));
	}

	/// <summary>
	/// Determines whether a given Pleiadean year is a leap year.
	/// </summary>
	public static bool IsLeapYear(int year)
	{
		// Leap year calculation based on Persian calendar: check if 1402 + year is leap
		return PersianCalendar.IsLeapYear(1402 + year);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		string[] monthNames = [ "Niloumehr", "Solaria", "Xuntaš", "Tarāxriz", "Lunaria", "Tārvan" ];

		var yearStr = Year switch
		{
			0 => "Zero", // Year Zero
			>1 => Year + " A.U.", // Anno Umbra	
			<1 => Year + " B.A.", // Before Awakening
			_ => throw new InvalidOperationException()
		};

		var dayStr = Day + (Day % 10) switch
		{
			1 when Day != 11 => "st",
			2 when Day != 12 => "nd",
			3 when Day != 13 => "rd",
			_ => "th"
		};

		var yearMark = (Year % 3) switch
		{
			2 => "Blood",
			1 => "Sweat",
			_ => "Tears",
		};
		
		return $"{dayStr} of {monthNames[Month - 1]}, {yearStr} (Year of {yearMark})";
	}
}
