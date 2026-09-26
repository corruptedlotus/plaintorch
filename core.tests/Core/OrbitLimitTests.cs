using Pleiades.Orbits;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The node limits of an orbit — <c>*x</c> caps a node's repetition, <c>@x</c> its emission — along with the two rules
/// they lean on: a top-level schedule starts at its first complete period, and a week inside a month is numbered from
/// the month's first complete week. The TypeScript reference engine (orbit-scheduler, <c>test/limits.test.ts</c>) pins
/// the same expectations.
/// </summary>
public sealed class OrbitLimitTests
{
	private static readonly OrbitGregorianCalendar Calendar = new();

	// "MM-dd Ddd", with " HH:mm" when the time is not midnight, so an expectation reads like a calendar.
	private static string Label(long ms)
	{
		var iso = JsDate.ToIsoString(ms);
		var time = iso[11..16];
		var day = DateTime.UnixEpoch.AddMilliseconds(ms).DayOfWeek.ToString()[..3];
		return $"{iso[5..10]}{(time == "00:00" ? "" : $" {time}")} {day}";
	}

	private static long Instant(string iso)
	{
		Assert.True(JsDate.TryParseIso(iso.Length == 10 ? $"{iso}T00:00:00Z" : iso, out var ms));
		return ms;
	}

	// The first `count` entries from `anchor` (an ISO date or instant), with "end" when the stream runs out first.
	private static List<string> Stream(string notation, string anchor, int count, uint seed = 1)
	{
		var engine = OrbitEngine.FromNotation(notation, Instant(anchor), Calendar, seed);
		var output = new List<string>();
		for (var i = 0; i < count; i++)
		{
			if (engine.Next() is not OrbitResolutionEntry entry)
			{
				output.Add("end");
				break;
			}

			output.Add(Label(entry.TimestampMs));
		}

		return output;
	}

	// --- *x: a node's repetition ---

	[Fact]
	public void A_bare_top_level_unit_repeats_x_times_then_the_stream_ends()
	{
		Assert.Equal(["09-28 Mon", "09-29 Tue", "09-30 Wed", "10-01 Thu", "10-02 Fri", "end"], Stream("d*5", "2026-09-28", 6));
	}

	[Fact]
	public void A_nested_bare_unit_repeats_from_the_start_of_every_parent_period()
	{
		// The first three days of every week, forever.
		Assert.Equal(
			["09-28 Mon", "09-29 Tue", "09-30 Wed", "10-05 Mon", "10-06 Tue", "10-07 Wed", "10-12 Mon"],
			Stream("w[d*3]", "2026-09-28", 7));
	}

	[Fact]
	public void The_parent_repeats_in_its_own_unit()
	{
		Assert.Equal(
			["09-28 Mon", "09-29 Tue", "09-30 Wed", "10-05 Mon", "10-06 Tue", "10-07 Wed", "end"],
			Stream("w[d*3]*2", "2026-09-28", 7));
	}

	[Fact]
	public void An_index_percent_does_not_step_is_a_run_of_one_so_star_does_nothing()
	{
		Assert.Equal(
			["10-02 Fri", "10-04 Sun", "10-06 Tue", "10-08 Thu", "11-02 Mon", "11-04 Wed"],
			Stream("d{2,4,6,8}*2", "2026-09-28", 6));

		// A range is a list of indices, not a run.
		Assert.Equal(["10-10 Sat", "10-11 Sun", "10-12 Mon", "10-13 Tue"], Stream("d{10~20}*3", "2026-09-28", 4));
	}

	[Fact]
	public void Percent_makes_an_index_repeat_and_star_keeps_its_first_steps_each_period()
	{
		Assert.Equal(
			["10-05 Mon", "10-08 Thu", "10-11 Sun", "10-14 Wed", "11-05 Thu", "11-08 Sun"],
			Stream("d{5}%3*4", "2026-09-28", 6));
		Assert.Equal(
			["09-28 09:00 Mon", "09-28 11:00 Mon", "09-28 13:00 Mon", "09-28 15:00 Mon", "09-29 09:00 Tue"],
			Stream("d[h{9}%2*4]", "2026-09-28", 5));
	}

	[Fact]
	public void A_repetition_counts_whether_or_not_its_children_fire_in_it()
	{
		// February has no 31st, but it is still the second of the three months.
		Assert.Equal(["01-31 Sat", "03-31 Tue", "end"], Stream("M[d{31}]*3", "2026-01-01", 3));
	}

	[Fact]
	public void Continuous_interval_steps_count_too()
	{
		Assert.Equal(["09-28 Mon", "09-30 Wed", "10-02 Fri", "end"], Stream("d%2*3", "2026-09-28", 4));
	}

	// --- @x: a node's emission ---

	[Fact]
	public void A_top_level_at_counts_once_then_the_stream_ends()
	{
		Assert.Equal(["10-02 Fri", "10-04 Sun", "end"], Stream("d{2,4,6,8}@2", "2026-09-28", 3));
	}

	[Fact]
	public void A_nested_at_counts_again_in_every_parent_period()
	{
		Assert.Equal(
			["09-28 09:00 Mon", "09-28 12:00 Mon", "09-29 09:00 Tue", "09-29 12:00 Tue", "09-30 09:00 Wed"],
			Stream("d[h{9,12,15,18}@2]", "2026-09-28", 5));
	}

	[Fact]
	public void Up_to_seven_times_a_month_from_the_months_first_complete_week()
	{
		Assert.Equal(
			["07-06 Mon", "07-08 Wed", "07-10 Fri", "07-12 Sun", "07-13 Mon", "07-15 Wed", "07-17 Fri", "08-03 Mon", "08-05 Wed"],
			Stream("M[w[d{1}%2]@7]", "2026-07-01", 9));
	}

	[Fact]
	public void At_counts_the_nodes_own_instances_before_an_enclosing_set_operation_acts()
	{
		// The 15th is one of July's seven, so excluding it leaves six.
		Assert.Equal(
			["07-06 Mon", "07-08 Wed", "07-10 Fri", "07-12 Sun", "07-13 Mon", "07-17 Fri", "08-03 Mon"],
			Stream("M[w[d{1}%2]@7]-d{15}", "2026-07-01", 7));
	}

	[Fact]
	public void A_spent_top_level_operand_lets_its_siblings_carry_on()
	{
		Assert.Equal(
			["10-01 Thu", "10-15 Thu", "11-01 Sun", "11-15 Sun", "12-15 Tue"],
			Stream("d{1}@2+d{15}", "2026-09-28", 5));
	}

	[Fact]
	public void A_literal_carries_its_limits()
	{
		Assert.Equal(["09-28 09:00 Mon", "09-29 09:00 Tue", "09-30 09:00 Wed", "end"], Stream("z{09:00}@3", "2026-09-28", 4));
	}

	[Fact]
	public void Zero_lets_nothing_through()
	{
		Assert.Equal(["end"], Stream("d*0", "2026-09-28", 1));
		Assert.Equal(["end"], Stream("d@0", "2026-09-28", 1));
	}

	// --- The top-level life: a schedule starts at its first complete period ---

	[Fact]
	public void A_period_one_of_whose_instances_is_already_past_is_skipped_whole()
	{
		Assert.Equal(["09-29 09:00 Tue", "09-29 17:00 Tue"], Stream("d[h{9,17}]", "2026-09-28T12:00:00Z", 2));
		Assert.Equal(["10-05 Mon", "10-09 Fri"], Stream("w[d{1,5}]", "2026-10-01", 2));
	}

	[Fact]
	public void A_period_whose_instances_are_all_ahead_is_kept()
	{
		Assert.Equal(["10-02 Fri", "10-09 Fri"], Stream("w[d{5}]", "2026-10-01", 2));
	}

	[Fact]
	public void A_top_level_star_counts_from_the_first_complete_period()
	{
		// Started on a Thursday: that week's Monday has passed, so the three weeks start with the next one.
		Assert.Equal(["10-05 Mon", "10-12 Mon", "10-19 Mon", "end"], Stream("w[d{1}]*3", "2026-10-01", 4));
		Assert.Equal(
			["10-05 Mon", "10-06 Tue", "10-07 Wed", "10-12 Mon", "10-13 Tue", "10-14 Wed", "end"],
			Stream("w[d*3]*2", "2026-10-01", 7));
	}

	[Fact]
	public void The_stream_starts_after_a_lower_bound_the_same_way()
	{
		Assert.Equal(["10-05 Mon", "10-09 Fri"], Stream("w[d{1,5}]>2026-10-01", "2026-09-28", 2));
	}

	// --- Weeks within a month ---

	[Fact]
	public void The_first_week_of_a_month_is_its_first_complete_one()
	{
		// July 2026 starts on a Wednesday: its first Monday is the 6th.
		Assert.Equal(["06-01 Mon", "07-06 Mon", "08-03 Mon", "09-07 Mon"], Stream("M[w{1}[d{1}]]", "2026-06-01", 4));

		// A week the month starts in counts when it holds every day asked for.
		Assert.Equal(["06-03 Wed", "07-01 Wed", "08-05 Wed"], Stream("M[w{1}[d{3}]]", "2026-06-01", 3));
	}

	[Fact]
	public void A_run_needs_its_starting_index_inside_the_month()
	{
		// July's partial first week lacks its Monday, so its Wednesday, Friday and Sunday do not fire either.
		Assert.Equal(["07-06 Mon", "07-08 Wed"], Stream("M[w[d{1}%2]]", "2026-07-01", 2));
	}

	[Fact]
	public void A_week_the_month_ends_in_is_cut_at_the_months_end()
	{
		// The week of Monday 27 July is July's: its Sunday, 2 August, fires in neither month.
		Assert.Equal(["07-27 Mon", "07-29 Wed", "07-31 Fri", "08-03 Mon"], Stream("M[w[d{1}%2]]", "2026-07-01", 16)[^4..]);
	}

	[Fact]
	public void A_top_level_week_index_addresses_its_implicit_month_but_a_bare_week_runs_across_months()
	{
		Assert.Equal(["06-01 Mon", "07-06 Mon", "08-03 Mon"], Stream("w{1}[d{1}]", "2026-06-01", 3));
		Assert.Equal(["07-27 Mon", "07-29 Wed", "07-31 Fri", "08-02 Sun", "08-03 Mon"], Stream("w[d{1}%2]", "2026-07-27", 5));
	}

	[Fact]
	public void A_week_index_never_overflows_into_the_next_month()
	{
		// Only months with five Mondays have a fifth week's Monday.
		Assert.Equal(["03-30 Mon", "06-29 Mon", "08-31 Mon"], Stream("M[w{5}[d{1}]]", "2026-01-01", 3));
	}

	[Fact]
	public void Pleiadean_months_number_their_saturday_weeks_the_same_way()
	{
		// The first Saturday of every Pleiadean month: a week the month starts in counts only when it holds its Saturday.
		var state = OrbitDays.CreateState("M[w{1}[d{1}]]", new DateOnly(2026, 7, 12), OrbitDays.Pleiadean, seed: 1);
		var (occurrences, _) = OrbitDays.SeekOccurrencesThrough(state, new DateOnly(2027, 7, 12), OrbitDays.Pleiadean);

		Assert.NotEmpty(occurrences);
		Assert.All(occurrences, item =>
		{
			Assert.Equal(DayOfWeek.Saturday, item.Date.DayOfWeek);
			// The first Saturday of a month falls within its first seven days.
			Assert.True(item.Date.AddDays(-7) < MonthStartBefore(item.Date), $"{item.Date} is not a first Saturday");
		});

		static DateOnly MonthStartBefore(DateOnly day)
		{
			// Walk back to the Pleiadean month start: the first day d at or before `day` where the day before is in another month.
			var probe = day;
			while (MonthOf(probe.AddDays(-1)) == MonthOf(probe))
			{
				probe = probe.AddDays(-1);
			}

			return probe;
		}

		static int MonthOf(DateOnly day)
			=> OrbitDays.Pleiadean.Get(JsDate.DaysFromCivil(day.Year, day.Month, day.Day) * JsDate.MsPerDay, OrbitUnit.Month);
	}

	// --- Calendar arithmetic ---

	[Fact]
	public void Stepping_a_month_from_the_31st_does_not_skip_the_next_month()
	{
		Assert.Equal(["09-01 09:00 Tue", "09-02 09:00 Wed"], Stream("d[h{9}]", "2026-08-31T10:00:00Z", 2));
		Assert.Equal(["02-05 Thu", "03-05 Thu"], Stream("M[d{5}]", "2026-01-31", 2));
	}

	// --- Persistence and previews: every limit is a pure function of (notation, epoch, seed) ---

	[Theory]
	[InlineData("M[w[d{1}%2]@7]")]
	[InlineData("w[d*3]*4")]
	[InlineData("d{2,4,6,8}@2")]
	[InlineData("w[d{#3}]")]
	[InlineData("d[h{9,12,15,18}@2]")]
	[InlineData("d{5}%3*4")]
	public void A_stream_resumed_at_every_step_matches_one_long_lived_engine(string notation)
	{
		var reference = Stream(notation, "2026-07-01", 60, seed: 7);
		var stepped = new List<string>();
		var blob = OrbitEngine.FromNotation(notation, Instant("2026-07-01"), Calendar, seed: 7).Serialize().ToJson();
		for (var i = 0; i < 60; i++)
		{
			var engine = OrbitEngine.Resume(OrbitSnapshot.FromJson(blob), Calendar);
			if (engine.Next() is not OrbitResolutionEntry entry)
			{
				stepped.Add("end");
				break;
			}

			stepped.Add(Label(entry.TimestampMs));
			blob = engine.Serialize().ToJson();
		}

		Assert.Equal(reference, stepped);
	}

	[Theory]
	[InlineData("M[w[d{1}%2]@7]")]
	[InlineData("w[d{#3}]")]
	[InlineData("d[h{9,12,15,18}@2]")]
	[InlineData("M[w{1}[d{1}]]")]
	public void A_far_preview_matches_the_stepped_stream(string notation)
	{
		var epoch = Instant("2026-07-01");
		var stepped = OrbitEngine.FromNotation(notation, epoch, Calendar, seed: 7)
			.NextWithin(epoch, Instant("2027-02-01"))
			.Cast<OrbitResolutionEntry>()
			.Where(entry => JsDate.ToIsoString(entry.TimestampMs).StartsWith("2027-01", StringComparison.Ordinal))
			.Select(entry => Label(entry.TimestampMs));
		var preview = OrbitEngine.FromNotation(notation, epoch, Calendar, seed: 7)
			.ResolveWithin(Instant("2027-01-01"), Instant("2027-02-01"))
			.Cast<OrbitResolutionEntry>()
			.Select(entry => Label(entry.TimestampMs));

		Assert.Equal(stepped, preview);
	}

	[Fact]
	public void A_snapshot_from_the_counting_engines_still_resumes_its_exhaustion_re_derived()
	{
		// The old engines counted @x over the whole stream, ending this one after seven.
		const string legacy = """
			{"version":1,"notation":"M[w[d{1}%2]@7]","epoch":"2026-07-01T00:00:00.000Z","seed":7,
			"cursor":"2026-07-18T00:00:00.000Z","emittedCount":7,"exhausted":true,"counters":{"2":{"fired":7}}}
			""";
		var engine = OrbitEngine.Resume(OrbitSnapshot.FromJson(legacy), Calendar);
		Assert.Equal("08-03 Mon", Label(Assert.IsType<OrbitResolutionEntry>(engine.Next()).TimestampMs));

		var rewritten = engine.Serialize();
		Assert.Null(rewritten.Counters);
		Assert.Null(rewritten.AutoReset);
	}

	[Fact]
	public void Only_a_top_level_limit_blocks_promotion()
	{
		Assert.False(OrbitEngine.FromNotation("w[d{1}]*3", 0, Calendar).IsAnchorStable());
		Assert.False(OrbitEngine.FromNotation("d{2,4}@1", 0, Calendar).IsAnchorStable());
		Assert.True(OrbitEngine.FromNotation("M[w[d{1}%2]@7]", 0, Calendar).IsAnchorStable());
		Assert.True(OrbitEngine.FromNotation("w[d*3]", 0, Calendar).IsAnchorStable());
	}

	[Fact]
	public void A_promoted_stream_continues_without_skipping_the_period_it_was_re_anchored_in()
	{
		var engine = OrbitEngine.FromNotation("w[d{1,3,5}]", Instant("2026-10-01"), Calendar, seed: 1);
		Assert.Equal("10-05 Mon", Label(Assert.IsType<OrbitResolutionEntry>(engine.Next()).TimestampMs));

		// Re-anchored on Monday the 5th, the resumed stream still owes that week's Wednesday and Friday.
		var resumed = OrbitEngine.Resume(engine.Promote(), Calendar);
		Assert.Equal("10-07 Wed", Label(Assert.IsType<OrbitResolutionEntry>(resumed.Next()).TimestampMs));
		Assert.Equal("10-09 Fri", Label(Assert.IsType<OrbitResolutionEntry>(resumed.Next()).TimestampMs));
	}
}
