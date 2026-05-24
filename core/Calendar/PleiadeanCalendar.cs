using System.Globalization;

namespace Pleiades.Calendar;

/// <summary>
/// Represents a date in the custom six-month Pleiadean calendar.
/// </summary>
public class PleiadeanCalendar
{
	private static readonly PersianCalendar PersianCalendar = new PersianCalendar();
	private static readonly string[] MonthNames = [ "Niloumehr", "Solaria", "Xuntaš", "Tarāxriz", "Lunaria", "Tārvan" ];
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
	/// Gets the Pleiadean month name.
	/// </summary>
	public string MonthName => MonthNames[Month - 1];

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
	/// Formats a Gregorian date using a Pleiadean format pattern.
	/// Supported tokens are <c>y</c>, <c>M</c>, and <c>d</c> with repeat counts controlling zero-padding.
	/// </summary>
	/// <param name="date">The Gregorian date to format.</param>
	/// <param name="format">The Pleiadean format pattern, such as <c>yyyMdd</c>.</param>
	/// <returns>The formatted Pleiadean date string.</returns>
	public static string Format(DateOnly date, string format)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(format);
		var pleiadean = FromDateTime(date.ToDateTime(TimeOnly.MinValue));
		var output = new System.Text.StringBuilder();

		for (var cursor = 0; cursor < format.Length;)
		{
			var token = format[cursor];
			if (token is 'y' or 'M' or 'd')
			{
				var width = 1;
				while (cursor + width < format.Length && format[cursor + width] == token)
				{
					width++;
				}

				var value = token switch
				{
					'y' => pleiadean.Year,
					'M' => pleiadean.Month,
					'd' => pleiadean.Day,
					_ => throw new InvalidOperationException($"Unsupported Pleiadean format token '{token}'."),
				};

				output.Append(value.ToString($"D{width}", CultureInfo.InvariantCulture));
				cursor += width;
				continue;
			}

			output.Append(token);
			cursor++;
		}

		return output.ToString();
	}

	/// <summary>
	/// Parses a Pleiadean date string with a format pattern into Gregorian <see cref="DateOnly"/>.
	/// Supported tokens are <c>y</c>, <c>M</c>, and <c>d</c> with repeat counts controlling consumed width.
	/// </summary>
	/// <param name="value">The formatted Pleiadean date string.</param>
	/// <param name="format">The format pattern used by the value.</param>
	/// <returns>The parsed Gregorian date.</returns>
	public static DateOnly Parse(string value, string format)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);
		ArgumentException.ThrowIfNullOrWhiteSpace(format);

		int? year = null;
		int? month = null;
		int? day = null;
		var valueCursor = 0;

		for (var formatCursor = 0; formatCursor < format.Length;)
		{
			var token = format[formatCursor];
			if (token is 'y' or 'M' or 'd')
			{
				var width = 1;
				while (formatCursor + width < format.Length && format[formatCursor + width] == token)
				{
					width++;
				}

				if (valueCursor + width > value.Length)
				{
					throw new FormatException($"Pleiadean date '{value}' does not match expected format '{format}'.");
				}

				var segment = value.Substring(valueCursor, width);
				if (!segment.All(char.IsDigit))
				{
					throw new FormatException($"Pleiadean date '{value}' has non-numeric token segment '{segment}'.");
				}

				var parsed = int.Parse(segment, CultureInfo.InvariantCulture);
				switch (token)
				{
					case 'y':
						year = parsed;
						break;
					case 'M':
						month = parsed;
						break;
					case 'd':
						day = parsed;
						break;
				}

				valueCursor += width;
				formatCursor += width;
				continue;
			}

			if (valueCursor >= value.Length || value[valueCursor] != token)
			{
				throw new FormatException($"Pleiadean date '{value}' does not match expected literal '{token}' in format '{format}'.");
			}

			valueCursor++;
			formatCursor++;
		}

		if (valueCursor != value.Length || year is null || month is null || day is null)
		{
			throw new FormatException($"Pleiadean date '{value}' does not fully match format '{format}'.");
		}

		var pleiadean = new PleiadeanCalendar(year.Value, month.Value, day.Value);
		return DateOnly.FromDateTime(pleiadean.ToDateTime());
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
		
		return $"{dayStr} of {MonthName}, {yearStr} (Year of {yearMark})";
	}
}
