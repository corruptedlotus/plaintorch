using Pleiades.Orbits;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Orbit units counted within a year — a calendar week (<c>y[w…]</c>), numbered from the year's first complete week as a
/// month's weeks are, and a day of the year (<c>y[d…]</c>) — and set operations nested inside a chain
/// (<c>M[d{15}+d{4}%4]</c>), which a span schedule reads exactly as its top-level form. The TypeScript reference engine
/// (orbit-scheduler, <c>test/limits.test.ts</c>) pins the same expectations.
/// </summary>
public sealed class OrbitNestingTests
{
	private static readonly OrbitGregorianCalendar Calendar = new();

	private static string Label(long ms)
	{
		var iso = JsDate.ToIsoString(ms);
		var time = iso[11..16];
		var day = DateTime.UnixEpoch.AddMilliseconds(ms).DayOfWeek.ToString()[..3];
		return $"{iso[..10]}{(time == "00:00" ? "" : $" {time}")} {day}";
	}

	private static long Instant(string iso)
	{
		Assert.True(JsDate.TryParseIso(iso.Length == 10 ? $"{iso}T00:00:00Z" : iso, out var ms));
		return ms;
	}

	private static List<string> Stream(string notation, string anchor, int count)
	{
		var engine = OrbitEngine.FromNotation(notation, Instant(anchor), Calendar, seed: 1);
		var output = new List<string>();
		for (var i = 0; i < count; i++)
		{
			switch (engine.Next())
			{
				case OrbitResolutionEntry entry:
					output.Add(Label(entry.TimestampMs));
					break;
				case OrbitSpanEntry span:
					output.Add($"{Label(span.StartMs)}-{JsDate.ToIsoString(span.EndMs)[11..16]}");
					break;
				default:
					output.Add("end");
					return output;
			}
		}

		return output;
	}

	// --- Calendar weeks: weeks within a year ---

	[Fact]
	public void The_first_calendar_week_is_the_years_first_complete_one()
	{
		// The first Monday of every year: 2026 starts on a Thursday, 2029 on a Monday.
		Assert.Equal(
			["2026-01-05 Mon", "2027-01-04 Mon", "2028-01-03 Mon", "2029-01-01 Mon", "2030-01-07 Mon"],
			Stream("y[w{1}[d{1}]]", "2026-01-01", 5));

		// A week the year starts in counts when it holds every day asked for: 2026's first Sunday is the 4th.
		Assert.Equal(["2026-01-04 Sun", "2027-01-03 Sun"], Stream("y[w{1}[d{7}]]", "2026-01-01", 2));
	}

	[Fact]
	public void A_calendar_week_index_counts_complete_weeks()
	{
		// A leaf week asks for the whole week, so 2026's partial first week is not its first: week 12 starts on 23 March.
		Assert.Equal(["2026-03-23 Mon", "2027-03-22 Mon"], Stream("y[w{12}]", "2026-01-01", 2));
		Assert.Equal(["2026-01-05 Mon", "2026-01-12 Mon", "2027-01-04 Mon"], Stream("y[w[d{1}]*2]", "2026-01-01", 3));
	}

	[Fact]
	public void A_week_the_year_ends_in_is_cut_at_the_years_end()
	{
		// The week of Monday 28 December 2026 is 2026's; its Friday, 1 January 2027, fires in neither year.
		Assert.Equal(
			["2026-12-28 Mon", "2026-12-30 Wed", "2027-01-04 Mon"],
			Stream("y[w[d{1}%2]]", "2026-01-01", 207)[^3..]);
	}

	[Fact]
	public void Pleiadean_calendar_weeks_start_on_saturday()
	{
		var state = OrbitDays.CreateState("y[w{1}[d{1}]]", new DateOnly(2026, 1, 1), OrbitDays.Pleiadean, seed: 1);
		var (occurrences, _) = OrbitDays.SeekOccurrencesThrough(state, new DateOnly(2030, 1, 1), OrbitDays.Pleiadean);

		Assert.NotEmpty(occurrences);
		Assert.All(occurrences, item =>
		{
			Assert.Equal(DayOfWeek.Saturday, item.Date.DayOfWeek);
			// The first Saturday of a Pleiadean year falls within its first seven days.
			var ms = JsDate.DaysFromCivil(item.Date.Year, item.Date.Month, item.Date.Day) * JsDate.MsPerDay;
			var yearStart = OrbitDays.Pleiadean.SnapToStart(ms, OrbitUnit.Year);
			Assert.InRange((ms - yearStart) / JsDate.MsPerDay, 0, 6);
		});
	}

	// --- A bare weekly schedule runs across months ---

	[Fact]
	public void A_bare_weekly_schedule_keeps_the_days_of_a_week_that_straddles_two_months()
	{
		// September 2026 ends on a Wednesday: its last week's Friday is 2 October, which stepping past the month's seam
		// used to lose.
		Assert.Equal(["2026-09-28 Mon", "2026-10-02 Fri", "2026-10-05 Mon"], Stream("w[d{1,5}]", "2026-09-28", 3));
		Assert.Equal(["2026-10-02 Fri", "2026-10-09 Fri"], Stream("w[d{5}]", "2026-09-28", 2));

		// A week inside a written month keeps its cut at the month's end.
		Assert.Equal(["2026-09-28 Mon", "2026-10-05 Mon"], Stream("M[w[d{1,5}]]", "2026-09-01", 8)[^2..]);
	}

	// --- Days within a year ---

	[Fact]
	public void A_day_within_a_year_is_its_day_of_the_year()
	{
		Assert.Equal(["2026-04-10 Fri", "2027-04-10 Sat", "2028-04-09 Sun"], Stream("y[d{100}]", "2026-01-01", 3));
		// Only a leap year has a 366th day.
		Assert.Equal(["2028-12-31 Sun", "2032-12-31 Fri"], Stream("y[d{366}]", "2026-01-01", 2));
		Assert.Equal(["2026-01-01 Thu", "2027-01-01 Fri"], Stream("y[d{1}]", "2026-01-01", 2));
	}

	// --- Set operations inside a chain ---

	[Fact]
	public void A_set_operation_inside_a_chain_combines_its_operands_within_the_parent()
	{
		Assert.Equal(
			["2026-07-04 Sat", "2026-07-08 Wed", "2026-07-12 Sun", "2026-07-15 Wed", "2026-07-16 Thu", "2026-07-20 Mon", "2026-07-24 Fri", "2026-07-28 Tue", "2026-08-04 Tue"],
			Stream("M[d{15}+d{4}%4]", "2026-07-01", 9));
		Assert.Equal(["2026-07-01 Wed", "2026-08-01 Sat"], Stream("M[d{1,15}-d{15}]", "2026-07-01", 2));
		Assert.Equal(["2026-07-01 Wed", "2026-07-02 Thu", "2026-07-06 Mon", "2026-07-07 Tue"], Stream("M[d{1~5}^d{3~7}]", "2026-07-01", 4));
	}

	[Fact]
	public void Nested_operands_keep_their_own_structure_and_limits()
	{
		Assert.Equal(
			["2026-07-06 Mon", "2026-07-17 Fri", "2026-08-03 Mon", "2026-08-21 Fri"],
			Stream("M[w{1}[d{1}]+w{3}[d{5}]]", "2026-07-01", 4));
		Assert.Equal(["2027-01-01 Fri", "2027-07-04 Sun"], Stream("y[M{1}[d{1}]+M{7}[d{4}]]", "2026-07-02", 2));

		// A limit above the set counts the set's combined instances; the top-level one counts once.
		Assert.Equal(["2026-07-04 Sat", "2026-07-08 Wed", "2026-07-12 Sun", "end"], Stream("M[d{15}+d{4}%4]@3", "2026-07-01", 4));
	}

	[Theory]
	[InlineData("d[h{9}=8h - h{12}=1h]", "d[h{9}]=8h - d[h{12}]=1h")]
	[InlineData("d[h{9}+h{17}]=1h", "d[h{9}]=1h + d[h{17}]=1h")]
	[InlineData("w[d{1}[h{9}=2h]+d{5}[h{14}=3h]]", "w[d{1}[h{9}]]=2h + w[d{5}[h{14}]]=3h")]
	public void A_span_set_inside_a_chain_reads_as_its_top_level_form(string nested, string topLevel)
	{
		Assert.Equal(Stream(topLevel, "2026-07-01", 12), Stream(nested, "2026-07-01", 12));
	}

	[Fact]
	public void A_span_set_inside_a_chain_keeps_the_top_level_constraints()
	{
		// A limit above the set would count the set's combined instances, which no top-level operand can say.
		Assert.Throws<FormatException>(() => OrbitEngine.FromNotation("d[h{9}=8h - h{12}=1h]@3", 0, Calendar));
		// Every operand spans, with exactly one duration.
		Assert.Throws<FormatException>(() => OrbitEngine.FromNotation("d[h{9}=8h + h{20}]", 0, Calendar));
		Assert.Throws<FormatException>(() => OrbitEngine.FromNotation("d[h{9}=8h - h{12}=1h]=2h", 0, Calendar));
	}

	[Fact]
	public void Nested_sets_pass_the_consumers_validation_tiers()
	{
		OrbitDays.ValidateDayGranularity("M[d{15}+d{4}%4]");
		OrbitDays.ValidateDayGranularity("y[w{1}[d{1}]+d{100}]");
		OrbitDays.ValidateGranular("d[h{9}+h{17}[m{30}]]");
		OrbitDays.ValidateParses("d[h{9}=8h - h{12}=1h]");

		Assert.Throws<FormatException>(() => OrbitDays.ValidateDayGranularity("M[d{15}+d{4}[h{9}]]"));
		Assert.Throws<FormatException>(() => OrbitDays.ValidateGranular("d[h{9}=8h - h{12}=1h]"));
	}
}
