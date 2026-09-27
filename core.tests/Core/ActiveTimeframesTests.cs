using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The core decides which timeframes are active right now (PEP100 patch 2): only while a Polaris cycle is strictly active,
/// only among the cycle's candidates, and only when the local time — truncated to the minute — lies in the timeframe's
/// <c>[start, end)</c> window. A window whose start is after its end wraps midnight; one whose start equals its end is
/// never active. Exclusivity is judged among the timeframes active right now: an active exclusive timeframe drops every
/// active non-exclusive one (all active exclusive ones stay), while an inactive exclusive one — outside its window, or
/// not a candidate of the cycle — suppresses nothing. Availability-mode timeframes are listed like any other. The
/// records carry <c>Exclusive</c> and keep the global listing's order.
/// </summary>
public sealed class ActiveTimeframesTests : VaultTestBase
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private static DateTimeOffset TodayAt(int hour, int minute, int second = 0)
		=> new(DateTime.Today.AddHours(hour).AddMinutes(minute).AddSeconds(second));

	private Task<T> Directives<T>(Func<IDirectiveApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IDirectiveApi>()));

	private Task<PolarisCycle> StartCycleAsync()
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IPolarisCycleApi>().StartNewAsync(TodayAt(0, 5), cancellationToken: Ct));

	private async Task<long> CreateAsync(string lunarId, string title, int startHour, int startMinute, int endHour, int endMinute, bool exclusive = false)
	{
		var timeframe = await Directives(api => api.CreateTimeframeAsync(
			lunarId,
			new TimeframePlan(title, new TimeOnly(startHour, startMinute), new TimeOnly(endHour, endMinute), Exclusive: exclusive),
			Ct));
		return timeframe.Id;
	}

	private async Task<IReadOnlyList<long>> ActiveAtAsync(DateTimeOffset at)
		=> (await Directives(api => api.ListActiveTimeframesAsync(at, Ct))).Select(record => record.Id).ToList();

	private Task<LunarDirective> LunarAsync(string title = "Moon Law")
		=> Directives(api => api.CreateLunarAsync(title, cancellationToken: Ct));

	[Fact]
	public async Task A_window_is_start_inclusive_and_end_exclusive_at_minute_precision()
	{
		var lunar = await LunarAsync();
		var morning = await CreateAsync(lunar.Id, "Morning", 9, 0, 12, 0);
		await StartCycleAsync();

		Assert.Equal([morning], await ActiveAtAsync(TodayAt(10, 30)));
		Assert.Equal([morning], await ActiveAtAsync(TodayAt(9, 0)));
		Assert.Empty(await ActiveAtAsync(TodayAt(8, 59, 59)));
		// Seconds are truncated: the last minute of the window is still inside, the end minute is not.
		Assert.Equal([morning], await ActiveAtAsync(TodayAt(11, 59, 59)));
		Assert.Empty(await ActiveAtAsync(TodayAt(12, 0)));
	}

	[Fact]
	public async Task An_overnight_window_wraps_midnight_on_both_sides()
	{
		var lunar = await LunarAsync();
		var night = await CreateAsync(lunar.Id, "Night", 22, 0, 6, 0);
		await StartCycleAsync();

		Assert.Equal([night], await ActiveAtAsync(TodayAt(23, 0)));
		Assert.Equal([night], await ActiveAtAsync(TodayAt(22, 0)));
		Assert.Equal([night], await ActiveAtAsync(TodayAt(5, 59)));
		Assert.Equal([night], await ActiveAtAsync(TodayAt(0, 0)));
		Assert.Empty(await ActiveAtAsync(TodayAt(6, 0)));
		Assert.Empty(await ActiveAtAsync(TodayAt(21, 59)));
		Assert.Empty(await ActiveAtAsync(TodayAt(12, 0)));
	}

	[Fact]
	public async Task A_window_whose_start_equals_its_end_is_never_active()
	{
		var lunar = await LunarAsync();
		await CreateAsync(lunar.Id, "Blink", 8, 0, 8, 0);
		await StartCycleAsync();

		Assert.Empty(await ActiveAtAsync(TodayAt(8, 0)));
		Assert.Empty(await ActiveAtAsync(TodayAt(7, 59)));
		Assert.Empty(await ActiveAtAsync(TodayAt(20, 0)));
	}

	[Fact]
	public async Task An_active_exclusive_timeframe_drops_the_active_non_exclusive_ones()
	{
		var lunar = await LunarAsync();
		var day = await CreateAsync(lunar.Id, "Day", 8, 0, 18, 0);
		var focus = await CreateAsync(lunar.Id, "Focus", 10, 0, 12, 0, exclusive: true);
		await StartCycleAsync();

		// Before the exclusive window opens it suppresses nothing.
		Assert.Equal([day], await ActiveAtAsync(TodayAt(9, 0)));

		var during = await Directives(api => api.ListActiveTimeframesAsync(TodayAt(11, 0), Ct));
		var record = Assert.Single(during);
		Assert.Equal(focus, record.Id);
		Assert.True(record.Exclusive);

		Assert.Equal([day], await ActiveAtAsync(TodayAt(12, 0)));
	}

	[Fact]
	public async Task Every_active_exclusive_timeframe_is_returned_in_listing_order()
	{
		var early = await LunarAsync("Alpha Law");
		var late = await LunarAsync("Beta Law");
		var plain = await CreateAsync(early.Id, "Plain", 8, 0, 18, 0);
		var second = await CreateAsync(late.Id, "Second", 9, 0, 12, 0, exclusive: true);
		var first = await CreateAsync(early.Id, "First", 10, 0, 12, 0, exclusive: true);
		var inactiveExclusive = await CreateAsync(late.Id, "Evening", 19, 0, 21, 0, exclusive: true);
		await StartCycleAsync();

		// Ordered like the global listing: directive title, then start time.
		Assert.Equal([first, second], await ActiveAtAsync(TodayAt(11, 0)));
		// Only one exclusive is active here; the other exclusives are outside their windows.
		Assert.Equal([second], await ActiveAtAsync(TodayAt(9, 30)));
		Assert.Equal([plain], await ActiveAtAsync(TodayAt(15, 0)));
		Assert.Equal([inactiveExclusive], await ActiveAtAsync(TodayAt(20, 0)));
	}

	[Fact]
	public async Task An_exclusive_timeframe_outside_the_cycle_candidates_suppresses_nothing()
	{
		var cycle = await StartCycleAsync();
		// The Pleiadean week starts on Saturday (d{1} = Saturday); the next day's weekday never selects the cycle day.
		var nextDay = DateOnly.FromDateTime(cycle.StartTime!.Value.LocalDateTime).AddDays(1);
		var nextWeekday = ((int)nextDay.DayOfWeek + 1) % 7 + 1;

		var lunar = await LunarAsync();
		var plain = await CreateAsync(lunar.Id, "Plain", 0, 0, 23, 59);
		var availability = (await Directives(api => api.CreateTimeframeAsync(
			lunar.Id,
			new TimeframePlan("Office Hours", new TimeOnly(0, 1), new TimeOnly(23, 59), AutoInclusion: TimeframeInclusion.Availability),
			Ct))).Id;
		await Directives(api => api.CreateTimeframeAsync(
			lunar.Id,
			new TimeframePlan("Deep Work", new TimeOnly(0, 0), new TimeOnly(23, 59), Orbit: $"w[d{{{nextWeekday}}}]", Exclusive: true),
			Ct));

		// Its window is open, but its orbit skips the cycle day, so it is not active and cannot drop the others; an
		// Availability-mode timeframe is listed like any other active one.
		Assert.Equal([plain, availability], await ActiveAtAsync(TodayAt(12, 0)));
	}

	[Fact]
	public async Task Nothing_is_active_without_a_strictly_active_cycle()
	{
		var lunar = await LunarAsync();
		await CreateAsync(lunar.Id, "Day", 0, 0, 23, 59);

		// No cycle at all.
		Assert.Empty(await ActiveAtAsync(TodayAt(12, 0)));

		// A planned (not begun) cycle for today.
		await Vault.WithScopeAsync(services => services.GetRequiredService<IPolarisCycleApi>().PlanAsync(DateOnly.FromDateTime(DateTime.Today).AddDays(-1), 1, cancellationToken: Ct));
		Assert.Empty(await ActiveAtAsync(TodayAt(12, 0)));

		// Begun, then ended.
		await StartCycleAsync();
		Assert.NotEmpty(await ActiveAtAsync(TodayAt(12, 0)));
		await Vault.WithScopeAsync(services => services.GetRequiredService<IPolarisCycleApi>().EndAsync(cancellationToken: Ct));
		Assert.Empty(await ActiveAtAsync(TodayAt(12, 0)));
	}

	[Fact]
	public async Task Active_records_are_shaped_like_the_global_listing()
	{
		var lunar = await LunarAsync();
		await CreateAsync(lunar.Id, "Morning", 9, 0, 12, 0, exclusive: true);
		await StartCycleAsync();

		var listed = Assert.Single(await Directives(api => api.ListAllTimeframesAsync(Ct)));
		var active = Assert.Single(await Directives(api => api.ListActiveTimeframesAsync(TodayAt(10, 0), Ct)));

		// Record equality compares the colleges list by reference, so the members are compared one by one.
		Assert.Equal(
			(listed.Id, listed.DirectiveId, listed.DirectiveTitle, listed.DirectiveStatus, listed.Title, listed.StartTime, listed.EndTime, listed.Orbit, listed.AutoInclusion, listed.Exclusive),
			(active.Id, active.DirectiveId, active.DirectiveTitle, active.DirectiveStatus, active.Title, active.StartTime, active.EndTime, active.Orbit, active.AutoInclusion, active.Exclusive));
		Assert.Equal(listed.AutoInclusionColleges, active.AutoInclusionColleges);
		Assert.True(active.Exclusive);
		Assert.Equal(lunar.Title, active.DirectiveTitle);
	}
}
