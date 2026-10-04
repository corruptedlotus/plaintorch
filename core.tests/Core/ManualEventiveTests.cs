using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orbits;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Materialization;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Covers manual eventives: occurrences added to a fate by hand rather than generated from its orbit. One is born
/// hardened with no RECURRENCE-ID, so it belongs to no recurrence set — it never dedups against, stands in for, or is
/// duplicated by a schedule-born occurrence on the same slot — and it is addressed by its row id alone. It is given
/// its whole <see cref="Epoch"/>: moment, granularity, duration and time zone.
/// </summary>
public sealed class ManualEventiveTests : VaultTestBase
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

	private static EventivePlan Plan(string fateId, DateTime moment, OrbitUnit granularity, string? duration = null, string? timeZone = null)
		=> new(fateId, new Epoch { Moment = moment, Granularity = granularity, Duration = duration, TimeZone = timeZone });

	private Task<Fate> CreateFateAsync(FatePlan plan)
		=> Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>().CreateFateAsync(plan, Ct));

	private Task<Eventive> AddEventiveAsync(EventivePlan plan)
		=> Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>().CreateEventiveAsync(plan, Ct));

	private Task<PolarisAgenda> AgendaAsync()
		=> Vault.WithScopeAsync(s => s.GetRequiredService<IPolarisCycleApi>().GetAgendaAsync(Ct));

	[Fact]
	public async Task A_manual_eventive_is_born_hardened_with_the_epoch_it_was_given()
	{
		var at = Today.AddDays(1).ToDateTime(new TimeOnly(18, 0));
		var fate = await CreateFateAsync(new FatePlan("Dentist"));

		var created = await AddEventiveAsync(Plan(fate.Id, at, OrbitUnit.Minute, "1h30m", "Europe/Berlin"));

		Assert.True(created.Id > 0);
		Assert.Null(created.RecurrenceId);
		Assert.True(created.IsManual);
		Assert.Equal(EventiveResolution.Pending, created.Resolution);

		var persisted = await Vault.QueryAsync(context => context.Eventives.AsNoTracking().SingleAsync(item => item.FateId == fate.Id, Ct));
		Assert.Equal(created.Id, persisted.Id);
		Assert.Null(persisted.RecurrenceId);
		Assert.Equal(at, persisted.Epoch.Moment);
		Assert.Equal(OrbitUnit.Minute, persisted.Epoch.Granularity);
		Assert.Equal("1h30m", persisted.Epoch.Duration);
		Assert.Equal("Europe/Berlin", persisted.Epoch.TimeZone);
	}

	[Fact]
	public async Task A_day_or_finer_moment_is_cut_to_the_start_of_its_unit()
	{
		var fate = await CreateFateAsync(new FatePlan("Holiday"));
		var day = Today.AddDays(2);

		var allDay = await AddEventiveAsync(Plan(fate.Id, day.ToDateTime(new TimeOnly(14, 37, 12)), OrbitUnit.Day));
		var hour = await AddEventiveAsync(Plan(fate.Id, day.ToDateTime(new TimeOnly(14, 37, 12)), OrbitUnit.Hour));

		Assert.Equal(day.ToDateTime(TimeOnly.MinValue), allDay.Epoch.Moment);
		Assert.True(allDay.Epoch.IsAllDay);
		Assert.Null(allDay.Epoch.Duration);
		Assert.Null(allDay.Epoch.TimeZone);
		Assert.Equal(day.ToDateTime(new TimeOnly(14, 0)), hour.Epoch.Moment);
	}

	[Fact]
	public async Task A_week_month_or_year_moment_is_kept_as_given()
	{
		// Where a week, month or year begins depends on the calendar it was chosen on, so the core does not move it.
		var fate = await CreateFateAsync(new FatePlan("Retreat"));
		var weekStart = Today.AddDays(3).ToDateTime(TimeOnly.MinValue);

		var week = await AddEventiveAsync(Plan(fate.Id, weekStart, OrbitUnit.Week, "3d"));

		Assert.Equal(weekStart, week.Epoch.Moment);
		Assert.Equal(OrbitUnit.Week, week.Epoch.Granularity);
		Assert.Equal("3d", week.Epoch.Duration);
	}

	[Fact]
	public async Task A_manual_eventive_joins_the_agenda_beside_its_fates_own_occurrence()
	{
		var tomorrow = Today.AddDays(1);
		var fate = await CreateFateAsync(new FatePlan("Daily sync", Orbit: "d"));

		await AddEventiveAsync(Plan(fate.Id, tomorrow.ToDateTime(TimeOnly.MinValue), OrbitUnit.Day));

		// The daily orbit projects tomorrow's occurrence; the manual one on the very same all-day slot has no
		// RECURRENCE-ID, so it neither overrides nor is overridden by it — both are listed.
		var agenda = await AgendaAsync();
		var tomorrows = agenda.Eventives.Where(item => item.FateId == fate.Id && item.Epoch.Date == tomorrow).ToList();
		Assert.Equal(2, tomorrows.Count);
		var manual = Assert.Single(tomorrows, item => item.IsManual);
		var projected = Assert.Single(tomorrows, item => !item.IsManual);
		Assert.Equal(tomorrow.ToDateTime(TimeOnly.MinValue), projected.RecurrenceId);
		Assert.NotNull(manual.Fate);
	}

	[Fact]
	public async Task The_harden_on_time_pass_neither_duplicates_nor_counts_a_manual_eventive()
	{
		var pastDay = Today.AddDays(-5);
		var fate = await CreateFateAsync(new FatePlan("Backdated", Date: pastDay, StartTime: new TimeOnly(9, 0), EventDuration: 30));
		await AddEventiveAsync(Plan(fate.Id, pastDay.ToDateTime(new TimeOnly(9, 0)), OrbitUnit.Minute));

		// The fate's own occurrence on the very moment the manual one sits still hardens (once), because the manual
		// eventive has no RECURRENCE-ID to be recognized as it.
		await Vault.WithScopeAsync(s => s.GetRequiredService<ProximityMaterializationService>().MaterializeForNowAsync(DateTimeOffset.Now, Ct));
		await Vault.WithScopeAsync(s => s.GetRequiredService<ProximityMaterializationService>().MaterializeForNowAsync(DateTimeOffset.Now, Ct));

		var rows = await Vault.QueryAsync(context => context.Eventives.AsNoTracking().Where(item => item.FateId == fate.Id).ToListAsync(Ct));
		Assert.Equal(2, rows.Count);
		Assert.Single(rows, item => item.RecurrenceId is null);
		Assert.Single(rows, item => item.RecurrenceId == pastDay.ToDateTime(new TimeOnly(9, 0)));
	}

	[Fact]
	public async Task A_manual_eventive_is_updated_by_its_row_id()
	{
		var fate = await CreateFateAsync(new FatePlan("Dentist"));
		var created = await AddEventiveAsync(Plan(fate.Id, Today.AddDays(1).ToDateTime(new TimeOnly(18, 0)), OrbitUnit.Minute));

		var updated = await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>().UpdateEventiveAsync(
			new EventiveOccurrenceRef(Id: created.Id),
			new EventiveUpdate(Date: Today.AddDays(3), Resolution: EventiveResolution.Cancelled),
			Ct));

		Assert.Equal(created.Id, updated.Id);
		var persisted = await Vault.QueryAsync(context => context.Eventives.AsNoTracking().SingleAsync(item => item.FateId == fate.Id, Ct));
		Assert.Null(persisted.RecurrenceId);
		Assert.Equal(Today.AddDays(3).ToDateTime(new TimeOnly(18, 0)), persisted.Epoch.Moment);
		Assert.Equal(EventiveResolution.Cancelled, persisted.Resolution);
	}

	[Fact]
	public async Task An_eventive_is_rescheduled_whole_and_validated_as_on_creation()
	{
		var fate = await CreateFateAsync(new FatePlan("Dentist"));
		var created = await AddEventiveAsync(Plan(fate.Id, Today.AddDays(1).ToDateTime(TimeOnly.MinValue), OrbitUnit.Day));
		Task<Eventive> RescheduleAsync(Epoch epoch) => Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>().UpdateEventiveAsync(
			new EventiveOccurrenceRef(Id: created.Id), new EventiveUpdate(Epoch: epoch), Ct));

		// Moment, granularity, duration and zone move together, and the moment is cut to the start of its new unit.
		var moved = await RescheduleAsync(new Epoch
		{
			Moment = Today.AddDays(4).ToDateTime(new TimeOnly(14, 37, 12)),
			Granularity = OrbitUnit.Minute,
			Duration = "45m",
			TimeZone = "Asia/Tehran",
		});

		Assert.Equal(created.Id, moved.Id);
		var persisted = await Vault.QueryAsync(context => context.Eventives.AsNoTracking().SingleAsync(item => item.FateId == fate.Id, Ct));
		Assert.Null(persisted.RecurrenceId);
		Assert.Equal(Today.AddDays(4).ToDateTime(new TimeOnly(14, 37)), persisted.Epoch.Moment);
		Assert.Equal(OrbitUnit.Minute, persisted.Epoch.Granularity);
		Assert.Equal("45m", persisted.Epoch.Duration);
		Assert.Equal("Asia/Tehran", persisted.Epoch.TimeZone);

		// Back to a floating whole day, with no duration of its own.
		await RescheduleAsync(new Epoch { Moment = Today.AddDays(5).ToDateTime(new TimeOnly(8, 0)), Granularity = OrbitUnit.Day });
		persisted = await Vault.QueryAsync(context => context.Eventives.AsNoTracking().SingleAsync(item => item.FateId == fate.Id, Ct));
		Assert.Equal(Today.AddDays(5).ToDateTime(TimeOnly.MinValue), persisted.Epoch.Moment);
		Assert.True(persisted.Epoch.IsAllDay);
		Assert.Null(persisted.Epoch.Duration);
		Assert.Null(persisted.Epoch.TimeZone);

		// A malformed duration or an unknown zone is refused, as on creation, and leaves the occurrence where it was.
		await Assert.ThrowsAsync<ArgumentException>(() => RescheduleAsync(new Epoch { Moment = Today.AddDays(6).ToDateTime(TimeOnly.MinValue), Granularity = OrbitUnit.Day, Duration = "1h 30m" }));
		await Assert.ThrowsAsync<ArgumentException>(() => RescheduleAsync(new Epoch { Moment = Today.AddDays(6).ToDateTime(TimeOnly.MinValue), Granularity = OrbitUnit.Day, TimeZone = "Mars/Olympus" }));
		persisted = await Vault.QueryAsync(context => context.Eventives.AsNoTracking().SingleAsync(item => item.FateId == fate.Id, Ct));
		Assert.Equal(Today.AddDays(5).ToDateTime(TimeOnly.MinValue), persisted.Epoch.Moment);
	}

	[Fact]
	public async Task An_attentive_is_rescheduled_whole_and_keeps_its_slot()
	{
		var decree = await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>().CreateDecreeAsync(new DecreePlan("Stretch", Orbit: "d[h{9}]"), Ct));
		var slot = Today.AddDays(2).ToDateTime(new TimeOnly(9, 0));

		var moved = await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>().UpdateAttentiveAsync(
			new AttentiveOccurrenceRef(decree.Id, slot),
			new AttentiveUpdate(Epoch: new Epoch { Moment = Today.AddDays(3).ToDateTime(new TimeOnly(17, 30)), Granularity = OrbitUnit.Minute, Duration = "20m" }),
			Ct));

		// The slot it was born on is its identity, so it still answers to it after the move.
		Assert.Equal(slot, moved.RecurrenceId);
		var persisted = await Vault.QueryAsync(context => context.Attentives.AsNoTracking().SingleAsync(item => item.DecreeId == decree.Id && item.RecurrenceId == slot, Ct));
		Assert.Equal(Today.AddDays(3).ToDateTime(new TimeOnly(17, 30)), persisted.Epoch.Moment);
		Assert.Equal(OrbitUnit.Minute, persisted.Epoch.Granularity);
		Assert.Equal("20m", persisted.Epoch.Duration);
	}

	[Fact]
	public async Task An_occurrence_reference_needs_a_row_id_or_an_owner_and_slot()
	{
		var fate = await CreateFateAsync(new FatePlan("Dentist"));

		await Assert.ThrowsAsync<ArgumentException>(() => Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>().UpdateEventiveAsync(
			new EventiveOccurrenceRef(OwnerId: fate.Id),
			new EventiveUpdate(Resolution: EventiveResolution.Cancelled),
			Ct)));
		await Assert.ThrowsAsync<InvalidOperationException>(() => Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>().UpdateEventiveAsync(
			new EventiveOccurrenceRef(Id: 404),
			new EventiveUpdate(Resolution: EventiveResolution.Cancelled),
			Ct)));
	}

	[Fact]
	public async Task A_cancelled_fate_takes_no_manual_eventive()
	{
		var tomorrow = Today.AddDays(1).ToDateTime(TimeOnly.MinValue);
		var fate = await CreateFateAsync(new FatePlan("Retired"));
		await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>().UpdateFateAsync(fate.Id, new FateUpdate(Status: FateStatus.Cancelled), Ct));

		await Assert.ThrowsAsync<InvalidOperationException>(() => AddEventiveAsync(Plan(fate.Id, tomorrow, OrbitUnit.Day)));
		await Assert.ThrowsAsync<InvalidOperationException>(() => AddEventiveAsync(Plan("no-such-fate", tomorrow, OrbitUnit.Day)));
		Assert.Equal(0, await Vault.QueryAsync(context => context.Eventives.CountAsync(Ct)));
	}

	[Theory]
	[InlineData("90", null, OrbitUnit.Day)]
	[InlineData("0h", null, OrbitUnit.Day)]
	[InlineData("1x", null, OrbitUnit.Day)]
	[InlineData("1h 30m", null, OrbitUnit.Day)]
	[InlineData(null, "Mars/Olympus_Mons", OrbitUnit.Day)]
	[InlineData(null, null, (OrbitUnit)0)]
	public async Task An_epoch_the_core_cannot_read_is_refused(string? duration, string? timeZone, OrbitUnit granularity)
	{
		var fate = await CreateFateAsync(new FatePlan("Dentist"));

		await Assert.ThrowsAsync<ArgumentException>(() => AddEventiveAsync(Plan(fate.Id, Today.ToDateTime(TimeOnly.MinValue), granularity, duration, timeZone)));
		Assert.Equal(0, await Vault.QueryAsync(context => context.Eventives.CountAsync(Ct)));
	}
}
