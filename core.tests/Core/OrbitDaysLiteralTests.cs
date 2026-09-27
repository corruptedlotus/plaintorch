using Pleiades.Calendar;
using Pleiades.Orbits;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// <see cref="OrbitDays.FixedLiteralDate(string)"/> and its calendar-aware overload name the day a lone <c>Z{y/M/d}</c>
/// literal falls on. The Gregorian one reads the numbers as a Gregorian date and is total (an impossible date is
/// <see langword="null"/>, never an exception); the calendar-aware one reads them on the given calendar exactly as the
/// engine resolves the literal there, so a Pleiadean-only day such as the 40th of a 61-day month has a date.
/// </summary>
public sealed class OrbitDaysLiteralTests
{
	private static DateOnly PleiadeanDay(int year, int month, int day)
		=> DateOnly.FromDateTime(new PleiadeanCalendar(year, month, day).ToDateTime());

	[Fact]
	public void The_gregorian_reading_returns_null_for_an_impossible_date_instead_of_throwing()
	{
		Assert.Null(OrbitDays.FixedLiteralDate("Z{2026/2/30}"));
		Assert.Null(OrbitDays.FixedLiteralDate("Z{2026/13/1}"));
		Assert.Null(OrbitDays.FixedLiteralDate("Z{2026/4/31T09:00}=1h"));
	}

	[Fact]
	public void The_gregorian_reading_keeps_a_valid_date_and_ignores_what_is_not_a_lone_literal()
	{
		Assert.Equal(new DateOnly(2027, 6, 5), OrbitDays.FixedLiteralDate("Z{2027/6/5T18:00}"));
		Assert.Equal(new DateOnly(2028, 2, 29), OrbitDays.FixedLiteralDate("Z{2028/2/29}=2h"));
		Assert.Null(OrbitDays.FixedLiteralDate("z{12:00}"));
		Assert.Null(OrbitDays.FixedLiteralDate("w[d{1~5}]"));
		Assert.Null(OrbitDays.FixedLiteralDate("Z{2027/6/5} + Z{2027/6/7}"));
		Assert.Null(OrbitDays.FixedLiteralDate("w[d{"));
	}

	[Fact]
	public void The_pleiadean_reading_resolves_a_pleiadean_only_day()
	{
		Assert.Equal(PleiadeanDay(3, 3, 40), OrbitDays.FixedLiteralDate("Z{3/3/40}", OrbitDays.Pleiadean));
		Assert.Equal(PleiadeanDay(3, 5, 61), OrbitDays.FixedLiteralDate("Z{3/5/61}", OrbitDays.Pleiadean));
	}

	[Fact]
	public void The_pleiadean_reading_agrees_with_the_day_the_engine_resolves()
	{
		var expected = PleiadeanDay(3, 3, 10);
		Assert.Equal(expected, OrbitDays.FixedLiteralDate("Z{3/3/10}", OrbitDays.Pleiadean));

		// The engine, anchored well before, resolves the literal's single occurrence to exactly that day.
		var state = OrbitDays.CreateState("Z{3/3/10}", expected.AddDays(-30), OrbitDays.Pleiadean);
		var occurrence = Assert.Single(OrbitDays.PreviewOccurrencesWithin(state, expected.AddDays(-30), expected.AddDays(30), OrbitDays.Pleiadean));
		Assert.Equal(expected, occurrence.Date);
	}

	[Fact]
	public void The_pleiadean_reading_returns_null_for_a_day_or_month_the_calendar_does_not_have()
	{
		Assert.Null(OrbitDays.FixedLiteralDate("Z{3/3/62}", OrbitDays.Pleiadean));
		Assert.Null(OrbitDays.FixedLiteralDate("Z{3/7/1}", OrbitDays.Pleiadean));
		Assert.Null(OrbitDays.FixedLiteralDate("Z{3/0/1}", OrbitDays.Pleiadean));
		Assert.Null(OrbitDays.FixedLiteralDate("Z{9000/1/1}", OrbitDays.Pleiadean));
	}

	[Fact]
	public void The_gregorian_calendar_overload_matches_the_gregorian_reading()
	{
		Assert.Equal(new DateOnly(2027, 6, 5), OrbitDays.FixedLiteralDate("Z{2027/6/5}", OrbitDays.Gregorian));
		Assert.Null(OrbitDays.FixedLiteralDate("Z{2026/2/30}", OrbitDays.Gregorian));
	}
}
