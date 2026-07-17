using Pleiades.Orbits;
using Xunit;

namespace Pleiades.Tests.Core;

public sealed class OrbitEngineTests
{
	private static readonly OrbitGregorianCalendar Calendar = new();
	private static long Epoch(int year, int month, int day) => JsDate.DaysFromCivil(year, month, day) * JsDate.MsPerDay;

	[Fact]
	public void Interval_streams_resolve_from_the_epoch_phase()
	{
		var engine = OrbitEngine.FromNotation("d%2", Epoch(2026, 7, 12), Calendar, seed: 1);
		var days = new List<string>();
		for (var i = 0; i < 4; i++)
		{
			var entry = Assert.IsType<OrbitResolutionEntry>(engine.Next());
			days.Add(JsDate.ToIsoString(entry.TimestampMs));
		}

		Assert.Equal(
			["2026-07-12T00:00:00.000Z", "2026-07-14T00:00:00.000Z", "2026-07-16T00:00:00.000Z", "2026-07-18T00:00:00.000Z"],
			days);
	}

	[Fact]
	public void Snapshot_resume_continues_the_stream_exactly()
	{
		var engine = OrbitEngine.FromNotation("w[d{#3}]", Epoch(2026, 7, 12), Calendar, seed: 0x0121337);
		var reference = new List<long>();
		for (var i = 0; i < 12; i++)
		{
			reference.Add(((OrbitResolutionEntry)engine.Next()!).TimestampMs);
		}

		var stepped = new List<long>();
		var blob = OrbitEngine.FromNotation("w[d{#3}]", Epoch(2026, 7, 12), Calendar, seed: 0x0121337).Serialize().ToJson();
		for (var i = 0; i < 12; i++)
		{
			var resumed = OrbitEngine.Resume(OrbitSnapshot.FromJson(blob), Calendar);
			stepped.Add(((OrbitResolutionEntry)resumed.Next()!).TimestampMs);
			blob = resumed.Serialize().ToJson();
		}

		Assert.Equal(reference, stepped);
	}

	[Fact]
	public void Preview_resolution_leaves_the_seeking_state_untouched()
	{
		var state = OrbitDays.CreateState("d%2", new DateOnly(2026, 7, 12), OrbitDays.Gregorian, seed: 7);

		// Preview far ahead of the cursor.
		Assert.True(OrbitDays.MatchesDay(state, new DateOnly(2026, 7, 20), OrbitDays.Gregorian));
		Assert.False(OrbitDays.MatchesDay(state, new DateOnly(2026, 7, 21), OrbitDays.Gregorian));

		// The state has not moved: seeking consumes everything pending from the epoch (catch-up included).
		var (occurrences, advanced) = OrbitDays.SeekOccurrencesThrough(state, new DateOnly(2026, 7, 14), OrbitDays.Gregorian);
		Assert.Equal([new DateOnly(2026, 7, 12)], occurrences.Select(item => item.Date));

		// And the advanced state continues past the consumed window.
		var (nextOccurrences, _) = OrbitDays.SeekOccurrencesThrough(advanced, new DateOnly(2026, 7, 17), OrbitDays.Gregorian);
		Assert.Equal([new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 16)], nextOccurrences.Select(item => item.Date));
	}

	[Fact]
	public void Validation_tiers_match_their_consumers()
	{
		// Strict day granularity (reflective schedules, timeframes).
		OrbitDays.ValidateDayGranularity("d%2");
		OrbitDays.ValidateDayGranularity("M[w{1}[d{4,7}]]%2");
		Assert.Throws<FormatException>(() => OrbitDays.ValidateDayGranularity("d[h{9}]"));
		Assert.Throws<FormatException>(() => OrbitDays.ValidateDayGranularity("z{9:30}"));
		Assert.Throws<FormatException>(() => OrbitDays.ValidateDayGranularity("d=8h"));
		Assert.Throws<FormatException>(() => OrbitDays.ValidateDayGranularity("not an orbit"));

		// Granular (decree orbits): any granularity, but no spans.
		OrbitDays.ValidateGranular("d[h{9}]");
		OrbitDays.ValidateGranular("w[d{2}]");
		Assert.Throws<FormatException>(() => OrbitDays.ValidateGranular("d[h{9}]=8h"));

		// Parse-only (fate orbits): spans welcome.
		OrbitDays.ValidateParses("d[h{9}]=8h");
		Assert.Throws<FormatException>(() => OrbitDays.ValidateParses("not an orbit"));
	}

	[Fact]
	public void Occurrences_carry_granularity_projections()
	{
		// Sub-day granularity: the occurrence carries its time of day.
		var timed = OrbitDays.CreateState("d[h{9,17}]", new DateOnly(2026, 7, 12), OrbitDays.Gregorian, seed: 1);
		var (timedOccurrences, _) = OrbitDays.SeekOccurrencesThrough(timed, new DateOnly(2026, 7, 13), OrbitDays.Gregorian);
		Assert.Equal(2, timedOccurrences.Count);
		Assert.Equal(new TimeOnly(9, 0), timedOccurrences[0].StartTime);
		Assert.Equal(new TimeOnly(17, 0), timedOccurrences[1].StartTime);

		// Super-day granularity: the occurrence spans its whole period.
		var weekly = OrbitDays.CreateState("w", new DateOnly(2026, 7, 13), OrbitDays.Gregorian, seed: 1); // a Monday
		var (weeklyOccurrences, _) = OrbitDays.SeekOccurrencesThrough(weekly, new DateOnly(2026, 7, 14), OrbitDays.Gregorian);
		var week = Assert.Single(weeklyOccurrences);
		Assert.Equal(new DateOnly(2026, 7, 13), week.Date);
		Assert.Equal(new DateOnly(2026, 7, 20), week.PeriodEndExclusive);
		Assert.Null(week.StartTime);

		// Span format: the occurrence carries start/end/duration.
		var span = OrbitDays.CreateState("d[h{9}]=8h", new DateOnly(2026, 7, 12), OrbitDays.Gregorian, seed: 1);
		var (spanOccurrences, advanced) = OrbitDays.SeekOccurrencesThrough(span, new DateOnly(2026, 7, 13), OrbitDays.Gregorian);
		var workday = Assert.Single(spanOccurrences);
		Assert.Equal(new TimeOnly(9, 0), workday.StartTime);
		Assert.Equal(new TimeOnly(17, 0), workday.EndTime);
		Assert.Equal(480, workday.DurationMinutes);

		// Span seeking is stateful through the manual cursor: the next window continues, no re-resolution.
		var (nextSpans, _) = OrbitDays.SeekOccurrencesThrough(advanced, new DateOnly(2026, 7, 15), OrbitDays.Gregorian);
		Assert.Equal(2, nextSpans.Count);
		Assert.Equal(new DateOnly(2026, 7, 13), nextSpans[0].Date);
		Assert.Equal(new DateOnly(2026, 7, 14), nextSpans[1].Date);
	}

	[Fact]
	public void Pleiadean_calendar_resolves_its_own_month_boundaries()
	{
		// Pleiadean months span 61 days, so "first day of every month" diverges from Gregorian quickly.
		var state = OrbitDays.CreateState("M[d{1}]", new DateOnly(2026, 7, 12), OrbitDays.Pleiadean, seed: 1);
		var (occurrences, _) = OrbitDays.SeekOccurrencesThrough(state, new DateOnly(2027, 7, 12), OrbitDays.Pleiadean);

		Assert.True(occurrences.Count is >= 5 and <= 7, $"expected ~6 Pleiadean month starts in a year, got {occurrences.Count}");
		for (var i = 1; i < occurrences.Count; i++)
		{
			var gap = occurrences[i].Date.DayNumber - occurrences[i - 1].Date.DayNumber;
			Assert.InRange(gap, 60, 61); // Pleiadean month lengths
		}

		// The same notation on the Gregorian calendar lands on Gregorian month starts instead.
		var gregorianState = OrbitDays.CreateState("M[d{1}]", new DateOnly(2026, 7, 12), OrbitDays.Gregorian, seed: 1);
		var (gregorianOccurrences, _) = OrbitDays.SeekOccurrencesThrough(gregorianState, new DateOnly(2026, 10, 15), OrbitDays.Gregorian);
		Assert.Equal(
			[new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)],
			gregorianOccurrences.Select(item => item.Date));
	}
}
