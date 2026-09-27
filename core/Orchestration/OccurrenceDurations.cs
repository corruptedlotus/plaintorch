using System.Text;
using System.Text.RegularExpressions;
using Pleiades.Orbits;

namespace Pleiades.Orchestration;

/// <summary>
/// Formats and applies the nominal, unit-typed durations stored on an <see cref="Epoch"/> (Orbit duration
/// notation such as <c>"8h"</c>, <c>"1d6h"</c>, <c>"1M"</c>). Arithmetic is civil (wall-clock) — matching the
/// codebase's naive time handling — so month/year parts add nominally via <see cref="DateTime"/> and are exact
/// for the common day-and-below spans.
/// </summary>
public static partial class OccurrenceDurations
{
	/// <summary>
	/// Formats a whole-minute length as compact Orbit duration notation (<c>480 -&gt; "8h"</c>,
	/// <c>1440 -&gt; "1d"</c>, <c>90 -&gt; "1h30m"</c>). Returns <see langword="null"/> for a null or
	/// non-positive length (an occurrence with no explicit span fills one granularity unit).
	/// </summary>
	public static string? Format(int? minutes)
	{
		if (minutes is not { } total || total <= 0)
		{
			return null;
		}

		var days = total / 1440;
		total %= 1440;
		var hours = total / 60;
		var mins = total % 60;

		var notation = new StringBuilder();
		if (days > 0)
		{
			notation.Append(days).Append('d');
		}

		if (hours > 0)
		{
			notation.Append(hours).Append('h');
		}

		if (mins > 0)
		{
			notation.Append(mins).Append('m');
		}

		return notation.Length == 0 ? null : notation.ToString();
	}

	/// <summary>
	/// The whole-minute span between two times of day, or <see langword="null"/> when either is absent or the end
	/// is not after the start.
	/// </summary>
	public static int? SpanMinutes(TimeOnly? start, TimeOnly? end)
		=> start is { } from && end is { } to && to > from ? (int)(to.ToTimeSpan() - from.ToTimeSpan()).TotalMinutes : null;

	/// <summary>
	/// Advances <paramref name="start"/> by a duration: the parsed notation when present, otherwise one unit of
	/// <paramref name="granularity"/> (the occurrence's implicit window). Unrecognized notation leaves the moment
	/// unchanged.
	/// </summary>
	public static DateTime Apply(DateTime start, string? duration, OrbitUnit granularity)
	{
		if (string.IsNullOrWhiteSpace(duration))
		{
			return AddUnit(start, granularity, 1);
		}

		var end = start;
		foreach (Match part in DurationPart().Matches(duration))
		{
			if (OrbitUnits.TryFromChar(part.Groups[2].Value[0], out var unit)
				&& int.TryParse(part.Groups[1].Value, out var count))
			{
				end = AddUnit(end, unit, count);
			}
		}

		return end;
	}

	private static DateTime AddUnit(DateTime moment, OrbitUnit unit, int count) => unit switch
	{
		OrbitUnit.Year => moment.AddYears(count),
		OrbitUnit.Month => moment.AddMonths(count),
		OrbitUnit.Week => moment.AddDays(7 * count),
		OrbitUnit.Day => moment.AddDays(count),
		OrbitUnit.Hour => moment.AddHours(count),
		OrbitUnit.Minute => moment.AddMinutes(count),
		OrbitUnit.Second => moment.AddSeconds(count),
		_ => moment,
	};

	[GeneratedRegex(@"(\d+)([yMwdhms])")]
	private static partial Regex DurationPart();
}
