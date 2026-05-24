using System.Globalization;
using Pleiades.Calendar;

namespace Pleiades.Puck;

/// <summary>
/// Encodes and decodes PUCK date-stamp numerators across supported calendar variants.
/// </summary>
public static class PuckDateStampCodec
{
	/// <summary>
	/// Formats a date according to a PUCK date-stamp kind.
	/// </summary>
	public static string Format(DateOnly date, PuckDateStampKind kind)
	{
		return kind switch
		{
			PuckDateStampKind.Gregorian => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
			PuckDateStampKind.Pleiadean => FormatPleiadean(date),
			_ => throw new InvalidOperationException($"Unsupported PUCK date-stamp kind '{kind}'."),
		};
	}

	/// <summary>
	/// Parses a date-stamp numerator into a Gregorian date.
	/// </summary>
	public static DateOnly Parse(string numerator, PuckDateStampKind kind)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(numerator);
		return kind switch
		{
			PuckDateStampKind.Gregorian => DateOnly.ParseExact(numerator, "yyyyMMdd", CultureInfo.InvariantCulture),
			PuckDateStampKind.Pleiadean => ParsePleiadean(numerator),
			_ => throw new InvalidOperationException($"Unsupported PUCK date-stamp kind '{kind}'."),
		};
	}

	private static string FormatPleiadean(DateOnly date)
	{
		return PleiadeanCalendar.Format(date, "yyyMdd");
	}

	private static DateOnly ParsePleiadean(string numerator)
	{
		return PleiadeanCalendar.Parse(numerator, "yyyMdd");
	}
}
