using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Calendar;
using Pleiades.Orbits;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Covers overriding a fate's or decree's calendar and time zone through the API. A recurrence only resolves on the
/// new calendar; a one-off <c>Z{y/M/d}</c> literal names a day on its calendar, so changing the calendar rewrites the
/// literal to keep that day, while a calendar sent together with a new orbit is the one that orbit is written in. The
/// time zone is a plain optional IANA zone, stored in the database and in the note's frontmatter.
/// </summary>
public sealed class DeclarativeCalendarAndZoneTests : VaultTestBase
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> WithApi<T>(Func<IDeclarativeApi, Task<T>> action)
		=> Vault.WithScopeAsync(s => action(s.GetRequiredService<IDeclarativeApi>()));

	private Task<Fate> ReadFateAsync(string id)
		=> Vault.QueryAsync(context => context.Fates.AsNoTracking().IgnoreAutoIncludes().SingleAsync(item => item.Id == id, Ct));

	/// <summary>A day ten days out, so a one-off on it is still upcoming and has a next occurrence.</summary>
	private static DateOnly Upcoming => DateOnly.FromDateTime(DateTime.Today).AddDays(10);

	/// <summary>The Pleiadean literal naming a Gregorian day, at a time of day.</summary>
	private static string PleiadeanLiteral(DateOnly day, string time)
	{
		var pleiadean = PleiadeanCalendar.FromDateTime(day.ToDateTime(TimeOnly.MinValue));
		return $"Z{{{pleiadean.Year}/{pleiadean.Month}/{pleiadean.Day}T{time}}}";
	}

	[Fact]
	public async Task A_recurring_fates_calendar_can_be_overridden_and_returned_to_the_default()
	{
		var fate = await WithApi(api => api.CreateFateAsync(new FatePlan("Market day", Orbit: "M[d{1}]"), Ct));
		Assert.Null(fate.Calendar);

		await WithApi(api => api.UpdateFateAsync(fate.Id, new FateUpdate(Calendar: DeclarativeCalendar.Gregorian), Ct));
		var overridden = await ReadFateAsync(fate.Id);
		Assert.Equal(DeclarativeCalendar.Gregorian, overridden.Calendar);
		Assert.Equal("M[d{1}]", overridden.Orbit);

		await WithApi(api => api.UpdateFateAsync(fate.Id, new FateUpdate(Calendar: new Optional<DeclarativeCalendar?>(null)), Ct));
		Assert.Null((await ReadFateAsync(fate.Id)).Calendar);
	}

	[Fact]
	public async Task Moving_a_one_off_onto_another_calendar_rewrites_its_literal_and_keeps_its_day()
	{
		var day = Upcoming;
		var fate = await WithApi(api => api.CreateFateAsync(new FatePlan("Dentist", Date: day, StartTime: new TimeOnly(18, 0), EndTime: new TimeOnly(19, 0)), Ct));
		var gregorian = $"Z{{{day.Year}/{day.Month}/{day.Day}T18:00}}=1h";
		Assert.Equal(gregorian, fate.Orbit);
		Assert.Equal(DeclarativeCalendar.Gregorian, fate.Calendar);

		await WithApi(api => api.UpdateFateAsync(fate.Id, new FateUpdate(Calendar: DeclarativeCalendar.Pleiadean), Ct));

		// The literal now names the same day in Pleiadean numbers, its time and span kept, and the fate still falls on it.
		var moved = await ReadFateAsync(fate.Id);
		Assert.Equal(DeclarativeCalendar.Pleiadean, moved.Calendar);
		Assert.Equal($"{PleiadeanLiteral(day, "18:00")}=1h", moved.Orbit);
		Assert.Equal(day.ToDateTime(new TimeOnly(18, 0)), moved.NextOccurrence);

		// And back: the Gregorian literal returns exactly.
		await WithApi(api => api.UpdateFateAsync(fate.Id, new FateUpdate(Calendar: DeclarativeCalendar.Gregorian), Ct));
		var back = await ReadFateAsync(fate.Id);
		Assert.Equal(gregorian, back.Orbit);
		Assert.Equal(day.ToDateTime(new TimeOnly(18, 0)), back.NextOccurrence);
	}

	[Fact]
	public async Task A_calendar_sent_with_a_new_orbit_is_the_one_that_orbit_is_written_in()
	{
		var day = Upcoming;
		var fate = await WithApi(api => api.CreateFateAsync(new FatePlan("Dentist", Orbit: "w[d{1}]"), Ct));
		var literal = PleiadeanLiteral(day, "09:30");

		await WithApi(api => api.UpdateFateAsync(fate.Id, new FateUpdate(Orbit: literal, Calendar: DeclarativeCalendar.Pleiadean), Ct));

		var updated = await ReadFateAsync(fate.Id);
		Assert.Equal(literal, updated.Orbit);
		Assert.Equal(DeclarativeCalendar.Pleiadean, updated.Calendar);
		// The cursor anchors on the literal read on the fate's own calendar, so the one-off is its next occurrence.
		Assert.Equal(day.ToDateTime(new TimeOnly(9, 30)), updated.NextOccurrence);
	}

	[Fact]
	public async Task A_new_orbit_keeps_a_chosen_calendar_and_undoes_only_a_one_offs_gregorian_pin()
	{
		var day = Upcoming;
		var gregorianLiteral = $"Z{{{day.Year}/{day.Month}/{day.Day}}}";

		// No calendar named: a one-off is pinned to Gregorian, and a recurrence replacing it returns to the default.
		var pinned = await WithApi(api => api.CreateFateAsync(new FatePlan("Pinned", Orbit: "d"), Ct));
		await WithApi(api => api.UpdateFateAsync(pinned.Id, new FateUpdate(Orbit: gregorianLiteral), Ct));
		Assert.Equal(DeclarativeCalendar.Gregorian, (await ReadFateAsync(pinned.Id)).Calendar);
		await WithApi(api => api.UpdateFateAsync(pinned.Id, new FateUpdate(Orbit: "d"), Ct));
		Assert.Null((await ReadFateAsync(pinned.Id)).Calendar);

		// A chosen calendar survives either way: a one-off is read on it, and a recurrence keeps it.
		var chosen = await WithApi(api => api.CreateFateAsync(new FatePlan("Chosen", Orbit: "d"), Ct));
		await WithApi(api => api.UpdateFateAsync(chosen.Id, new FateUpdate(Calendar: DeclarativeCalendar.Pleiadean), Ct));
		var pleiadeanLiteral = PleiadeanLiteral(day, "12:00");
		await WithApi(api => api.UpdateFateAsync(chosen.Id, new FateUpdate(Orbit: pleiadeanLiteral), Ct));
		var oneOff = await ReadFateAsync(chosen.Id);
		Assert.Equal(DeclarativeCalendar.Pleiadean, oneOff.Calendar);
		Assert.Equal(day.ToDateTime(new TimeOnly(12, 0)), oneOff.NextOccurrence);
		await WithApi(api => api.UpdateFateAsync(chosen.Id, new FateUpdate(Orbit: "w[d{1}]"), Ct));
		Assert.Equal(DeclarativeCalendar.Pleiadean, (await ReadFateAsync(chosen.Id)).Calendar);
	}

	[Fact]
	public async Task A_fates_and_a_decrees_time_zone_is_set_cleared_and_checked()
	{
		var fate = await WithApi(api => api.CreateFateAsync(new FatePlan("Call", Orbit: "d"), Ct));
		await WithApi(api => api.UpdateFateAsync(fate.Id, new FateUpdate(TimeZone: "Europe/Berlin"), Ct));
		Assert.Equal("Europe/Berlin", (await ReadFateAsync(fate.Id)).TimeZone);
		await WithApi(api => api.UpdateFateAsync(fate.Id, new FateUpdate(TimeZone: new Optional<string?>(null)), Ct));
		Assert.Null((await ReadFateAsync(fate.Id)).TimeZone);
		await Assert.ThrowsAsync<ArgumentException>(() => WithApi(api => api.UpdateFateAsync(fate.Id, new FateUpdate(TimeZone: "Mars/Olympus_Mons"), Ct)));

		var decree = await WithApi(api => api.CreateDecreeAsync(new DecreePlan("Stretch", Orbit: "d"), Ct));
		await WithApi(api => api.UpdateDecreeAsync(decree.Id, new DecreeUpdate(Calendar: DeclarativeCalendar.Gregorian, TimeZone: "Asia/Tehran"), Ct));
		var stored = await Vault.QueryAsync(context => context.Decrees.AsNoTracking().IgnoreAutoIncludes().SingleAsync(item => item.Id == decree.Id, Ct));
		Assert.Equal(DeclarativeCalendar.Gregorian, stored.Calendar);
		Assert.Equal("Asia/Tehran", stored.TimeZone);
		Assert.Equal("d", stored.Orbit);
	}

	[Fact]
	public async Task The_time_zone_round_trips_through_the_notes_frontmatter()
	{
		var fate = await WithApi(api => api.CreateFateAsync(new FatePlan("Zoned Event", Orbit: "w[d{2}]"), Ct));
		await WithApi(api => api.BeginFateBoundaryAsync(fate.Id, Ct));
		await WithApi(api => api.UpdateFateAsync(fate.Id, new FateUpdate(Calendar: DeclarativeCalendar.Gregorian, TimeZone: "Europe/Berlin"), Ct));

		var markdown = Vault.ReadVaultFile("Fates/Zoned Event.md");
		Assert.Contains("timezone: Europe/Berlin", markdown);
		Assert.Contains("calendar: Gregorian", markdown);

		// An edit to the note is read back into the database.
		Vault.WriteVaultFile("Fates/Zoned Event.md", markdown.Replace("timezone: Europe/Berlin", "timezone: Asia/Tehran", StringComparison.Ordinal));
		await Vault.ReconcileAsync(Vault.AbsolutePath("Fates/Zoned Event.md"));
		Assert.Equal("Asia/Tehran", (await ReadFateAsync(fate.Id)).TimeZone);
	}

	[Fact]
	public void A_literal_is_rewritten_between_calendars_with_its_time_and_modifiers_kept()
	{
		var gregorian = "Z{2026/10/2T18:30:15}=1h30m";
		var pleiadean = OrbitDays.RewriteFixedLiteral(gregorian, OrbitDays.Gregorian, OrbitDays.Pleiadean);

		Assert.NotNull(pleiadean);
		Assert.Equal(new DateOnly(2026, 10, 2), OrbitDays.FixedLiteralDate(pleiadean!, OrbitDays.Pleiadean));
		Assert.EndsWith("T18:30:15}=1h30m", pleiadean, StringComparison.Ordinal);
		Assert.Equal(gregorian, OrbitDays.RewriteFixedLiteral(pleiadean!, OrbitDays.Pleiadean, OrbitDays.Gregorian));

		// Not a lone literal: nothing to rewrite.
		Assert.Null(OrbitDays.RewriteFixedLiteral("M[d{1}]", OrbitDays.Gregorian, OrbitDays.Pleiadean));
	}
}
