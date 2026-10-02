using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orbits;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Materialization;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Covers pinpointing a zoned occurrence: a timed epoch anchored in a time zone names an absolute instant (its moment
/// read on that zone's clock), and everything that compares occurrences with this host's "now" or "today" — the
/// agenda, the Polaris inclusion window, an eventive's begun/finished phase — reads it there. Floating and all-day
/// epochs are unchanged.
/// </summary>
public sealed class ZonedOccurrenceTimeTests : VaultTestBase
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private static Epoch Timed(DateTime moment, string? timeZone, string? duration = null)
		=> new() { Moment = moment, Granularity = OrbitUnit.Minute, Duration = duration, TimeZone = timeZone };

	[Fact]
	public void A_zoned_timed_epoch_names_the_instant_its_zone_reads()
	{
		// Tehran keeps +03:30 all year.
		var epoch = Timed(new DateTime(2026, 10, 2, 18, 30, 0), "Asia/Tehran");

		Assert.Equal(new DateTimeOffset(2026, 10, 2, 15, 0, 0, TimeSpan.Zero), epoch.Instant);
		Assert.Equal(TimeZoneInfo.ConvertTime(epoch.Instant!.Value, TimeZoneInfo.Local).DateTime, epoch.LocalMoment);
		Assert.Equal(DateOnly.FromDateTime(epoch.LocalMoment), epoch.LocalDate);
	}

	[Fact]
	public void Floating_all_day_and_unknown_zones_name_no_instant()
	{
		var floating = Timed(new DateTime(2026, 10, 2, 18, 30, 0), null);
		var allDay = new Epoch { Moment = new DateTime(2026, 10, 2), Granularity = OrbitUnit.Day, TimeZone = "Asia/Tehran" };
		var unknown = Timed(new DateTime(2026, 10, 2, 18, 30, 0), "Mars/Olympus_Mons");

		Assert.Null(floating.Instant);
		Assert.Equal(floating.Moment, floating.LocalMoment);
		Assert.Null(allDay.Instant);
		Assert.Equal(allDay.Moment, allDay.LocalMoment);
		Assert.Null(unknown.Instant);
	}

	[Fact]
	public void A_skipped_wall_time_lands_later_and_a_repeated_one_is_read_first()
	{
		// Berlin springs forward at 02:00 on 29 March 2026 (to +02:00) and falls back at 03:00 on 25 October (to +01:00).
		var skipped = Timed(new DateTime(2026, 3, 29, 2, 30, 0), "Europe/Berlin");
		var repeated = Timed(new DateTime(2026, 10, 25, 2, 30, 0), "Europe/Berlin");

		Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), skipped.Instant);
		Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), repeated.Instant);
	}

	[Fact]
	public void An_anchored_eventive_has_begun_at_its_instant_not_its_wall_time()
	{
		// Wall time two hours ahead of UTC's, but on a +14:00 clock: twelve hours ago.
		var now = DateTime.UtcNow;
		var eventive = new Eventive
		{
			FateId = "f",
			Epoch = Timed(now.AddHours(2), "Pacific/Kiritimati", "1h"),
		};
		var phases = new EventiveLifecyclePhaseSource();

		Assert.True(phases.HasBegun(eventive));
		Assert.True(phases.HasFinished(eventive));

		// Floating, the same wall time still reads as two hours ahead.
		eventive.Epoch = Timed(now.AddHours(2), null, "1h");
		Assert.False(phases.HasBegun(eventive));
	}

	[Fact]
	public void The_polaris_window_takes_a_zoned_eventive_at_the_instant_it_names()
	{
		var windowStart = DateTime.Today.AddHours(8);
		var windowEnd = windowStart.AddHours(24);
		var kiritimati = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Kiritimati");

		// Two hours into the window here, written on Kiritimati's clock — a wall time far from the window's own.
		var inside = TimeZoneInfo.ConvertTime(new DateTimeOffset(windowStart.AddHours(2)), kiritimati).DateTime;
		var outside = TimeZoneInfo.ConvertTime(new DateTimeOffset(windowEnd.AddHours(2)), kiritimati).DateTime;

		Assert.True(InclusionWindow.EventiveIntersects(new Eventive { FateId = "f", Epoch = Timed(inside, "Pacific/Kiritimati") }, windowStart, windowEnd));
		Assert.False(InclusionWindow.EventiveIntersects(new Eventive { FateId = "f", Epoch = Timed(outside, "Pacific/Kiritimati") }, windowStart, windowEnd));
	}

	[Fact]
	public async Task The_agenda_lists_a_zoned_eventive_on_the_day_it_falls_here()
	{
		var today = DateOnly.FromDateTime(DateTime.Today);
		var horizonEnd = today.AddDays(8).ToDateTime(TimeOnly.MinValue);
		var kiritimati = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Kiritimati");
		var pagoPago = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Pago_Pago");

		var fate = await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>().CreateFateAsync(new FatePlan("Call"), Ct));

		// One hour before the horizon ends here, written on Kiritimati's clock: its wall date is a day past the horizon.
		var inside = TimeZoneInfo.ConvertTime(new DateTimeOffset(horizonEnd.AddHours(-1)), kiritimati).DateTime;
		// One hour after the horizon ends here, written on Pago Pago's clock: its wall date is still inside the horizon.
		var outside = TimeZoneInfo.ConvertTime(new DateTimeOffset(horizonEnd.AddHours(1)), pagoPago).DateTime;
		await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>().CreateEventiveAsync(new EventivePlan(fate.Id, Timed(inside, "Pacific/Kiritimati")), Ct));
		await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>().CreateEventiveAsync(new EventivePlan(fate.Id, Timed(outside, "Pacific/Pago_Pago")), Ct));
		Assert.True(DateOnly.FromDateTime(inside) > today.AddDays(7) || DateOnly.FromDateTime(outside) <= today.AddDays(7));

		var agenda = await Vault.WithScopeAsync(s => s.GetRequiredService<IPolarisCycleApi>().GetAgendaAsync(Ct));
		var listed = Assert.Single(agenda.Eventives, item => item.FateId == fate.Id);
		Assert.Equal("Pacific/Kiritimati", listed.Epoch.TimeZone);
		Assert.Equal(today.AddDays(7), listed.Epoch.LocalDate);
	}

	private Task<T> WithApi<T>(Func<IDeclarativeApi, Task<T>> action)
		=> Vault.WithScopeAsync(s => action(s.GetRequiredService<IDeclarativeApi>()));

	private async Task<Fate> ZonedFateAsync(string orbit, string timeZone)
	{
		var fate = await WithApi(api => api.CreateFateAsync(new FatePlan("Call", Orbit: orbit), Ct));
		return await WithApi(api => api.UpdateFateAsync(fate.Id, new FateUpdate(TimeZone: timeZone), Ct));
	}

	[Fact]
	public async Task A_zoned_fate_stamps_its_projected_occurrences_and_the_agenda_lists_them_by_the_day_they_fall_here()
	{
		var today = DateOnly.FromDateTime(DateTime.Today);
		var horizon = today.AddDays(7);
		var kiritimati = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Kiritimati");
		var fate = await ZonedFateAsync("d[h{1}]", "Pacific/Kiritimati");

		var agenda = await Vault.WithScopeAsync(s => s.GetRequiredService<IPolarisCycleApi>().GetAgendaAsync(Ct));
		var listed = agenda.Eventives.Where(item => item.FateId == fate.Id).ToList();

		// 01:00 each day on Kiritimati's clock (+14:00), listed when that instant falls within the horizon here: the
		// horizon's day-after on Kiritimati's clock is usually its last day on this one.
		var expected = Enumerable.Range(0, 11)
			.Select(offset => today.AddDays(offset).ToDateTime(new TimeOnly(1, 0)))
			.Where(wall => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(wall, kiritimati, TimeZoneInfo.Local)) is var day
				&& day >= today && day <= horizon)
			.ToList();
		Assert.Equal(expected, listed.Select(item => item.RecurrenceId!.Value).ToList());
		Assert.All(listed, item =>
		{
			Assert.Equal("Pacific/Kiritimati", item.Epoch.TimeZone);
			Assert.Equal(new DateTimeOffset(item.Epoch.Moment, TimeSpan.FromHours(14)), item.Epoch.Instant);
		});
	}

	[Fact]
	public async Task A_zoned_fate_hardens_an_occurrence_once_its_instant_has_passed()
	{
		var kiritimati = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Kiritimati");
		var fate = await ZonedFateAsync("h", "Pacific/Kiritimati");
		// A quarter past the hour here keeps Kiritimati's clock off the hour, whatever this host's offset.
		var now = new DateTimeOffset(DateTime.Today.AddHours(DateTime.Now.Hour).AddMinutes(15));

		var savedFrom = TimeZoneInfo.ConvertTime(DateTimeOffset.Now, kiritimati).DateTime;
		await Vault.WithScopeAsync(s => s.GetRequiredService<ProximityMaterializationService>().MaterializeForNowAsync(now, Ct));
		var savedBy = TimeZoneInfo.ConvertTime(DateTimeOffset.Now, kiritimati).DateTime;
		var hardened = await Vault.QueryAsync(context => context.Eventives.Where(item => item.FateId == fate.Id).ToListAsync(Ct));
		var stored = await Vault.QueryAsync(context => context.Fates.SingleAsync(item => item.Id == fate.Id, Ct));

		// Hourly on Kiritimati's clock: every hour whose instant has passed hardens, stamped with the zone. The next
		// occurrence is an hour on that clock too, the first after the save that refreshed it (it reads the real clock).
		var kiritimatiNow = TimeZoneInfo.ConvertTime(now, kiritimati).DateTime;
		var lastPassed = kiritimatiNow.Date.AddHours(kiritimatiNow.Hour);
		Assert.NotEmpty(hardened);
		Assert.All(hardened, item =>
		{
			Assert.Equal("Pacific/Kiritimati", item.Epoch.TimeZone);
			Assert.True(item.Epoch.Instant <= now);
		});
		Assert.Equal(lastPassed, hardened.Max(item => item.Epoch.Moment));
		var next = Assert.NotNull(stored.NextOccurrence);
		Assert.Equal(0, next.Minute);
		Assert.InRange(next, savedFrom, savedBy.AddHours(1));
	}

	[Fact]
	public async Task Interacting_with_a_projected_occurrence_of_a_zoned_fate_hardens_it_in_that_zone()
	{
		var fate = await ZonedFateAsync("d[h{9}]", "Asia/Tokyo");
		var slot = DateTime.SpecifyKind(DateTime.Today.AddDays(3).AddHours(9), DateTimeKind.Unspecified);

		var updated = await WithApi(api => api.UpdateEventiveAsync(
			new EventiveOccurrenceRef(fate.Id, slot),
			new EventiveUpdate(Resolution: EventiveResolution.Cancelled), Ct));

		Assert.Equal(slot, updated.RecurrenceId);
		Assert.Equal("Asia/Tokyo", updated.Epoch.TimeZone);
		Assert.Equal(new DateTimeOffset(slot, TimeSpan.FromHours(9)), updated.Epoch.Instant);
	}
}
