using Pleiades.Calendar;
using Pleiades.Puck;

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
		return $"Polaris {FormatPleiadeanDate(date)}";
	}

	/// <summary>
	/// Formats a Gregorian date as a compact Pleiadean date label using <c>y, MMMM d</c> semantics.
	/// </summary>
	public static string FormatPleiadeanDate(DateOnly date)
	{
		var pleiadeanDate = PleiadeanCalendar.FromDateTime(date.ToDateTime(TimeOnly.MinValue));
		return $"{pleiadeanDate.Day} of {pleiadeanDate.MonthName} {pleiadeanDate.Year}";
	}

	/// <summary>
	/// Attempts to resolve an existing Polaris PUCK date-stamp identifier into a date.
	/// </summary>
	public static bool TryResolvePolarisDate(string? id, out DateOnly date)
	{
		date = default;
		if (string.IsNullOrWhiteSpace(id))
		{
			return false;
		}

		try
		{
			date = PuckDateStampCodec.Parse(id.Trim(), PuckDateStampKind.Pleiadean);
			return true;
		}
		catch (FormatException)
		{
			return false;
		}
		catch (InvalidOperationException)
		{
			return false;
		}
	}
}