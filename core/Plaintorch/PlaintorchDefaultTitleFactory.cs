using System.Globalization;
using Pleiades.Calendar;

namespace Pleiades.Plaintorch;

/// <summary>
/// Produces centralized default PLAINTORCH titles for date-oriented entities.
/// </summary>
public static class PlaintorchDefaultTitleFactory
{
	/// <summary>
	/// Creates the default onrush title for a date.
	/// </summary>
	public static string CreateOnrushTitle(DateOnly date)
	{
		return $"Onrush {FormatPleiadeanDate(date)}";
	}

	/// <summary>
	/// Creates the default Polaris title for a date.
	/// </summary>
	public static string CreatePolarisTitle(DateOnly date)
	{
		return $"Polaris Cycle {FormatPleiadeanDate(date)}";
	}

	/// <summary>
	/// Formats a Gregorian date as a compact Pleiadean date label using <c>y, MMMM d</c> semantics.
	/// </summary>
	public static string FormatPleiadeanDate(DateOnly date)
	{
		var pleiadeanDate = PleiadeanCalendar.FromDateTime(date.ToDateTime(TimeOnly.MinValue));
		return $"{pleiadeanDate.Year}, {pleiadeanDate.MonthName} {pleiadeanDate.Day}";
	}

	/// <summary>
	/// Attempts to resolve an existing Gregorian date-stamp PUCK identifier into a date.
	/// </summary>
	public static bool TryResolveGregorianDate(string? id, out DateOnly date)
	{
		return DateOnly.TryParseExact(id, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
	}
}