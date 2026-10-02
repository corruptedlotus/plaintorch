using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Polaris activation routes through one service-level path: starting a cycle activates the one already planned for the
/// day rather than duplicating it (the id IS the day), and adding to "the current cycle" with none active starts one.
/// Today's cycle is current while it is planned or running, never once it has concluded.
/// </summary>
public sealed class PolarisActivationTests : VaultTestBase
{
	private Task<T> PolarisAsync<T>(Func<IPolarisCycleApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IPolarisCycleApi>()));

	private Task<int> CycleCountAsync()
		=> Vault.QueryAsync(context => context.PolarisCycles.CountAsync(TestContext.Current.CancellationToken));

	private Task<PolarisCycle> SingleCycleAsync()
		=> Vault.QueryAsync(context => context.PolarisCycles.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken));

	[Fact]
	public async Task Start_new_activates_a_cycle_planned_for_today_instead_of_duplicating_it()
	{
		var today = DateOnly.FromDateTime(DateTime.Today);
		// A forecast whose target lands on today: a cycle planned for the day, not yet started (its id IS today).
		var planned = await PolarisAsync(api => api.PlanAsync(today.AddDays(-1), 1, cancellationToken: TestContext.Current.CancellationToken));
		Assert.Null(planned.StartTime);

		var started = await PolarisAsync(api => api.StartNewAsync(cancellationToken: TestContext.Current.CancellationToken));

		Assert.Equal(planned.Id, started.Id);   // the planned cycle was activated, not duplicated
		Assert.NotNull(started.StartTime);
		Assert.Equal(1, await CycleCountAsync());
	}

	[Fact]
	public async Task Start_new_with_nothing_planned_creates_and_activates_a_cycle()
	{
		var started = await PolarisAsync(api => api.StartNewAsync(cancellationToken: TestContext.Current.CancellationToken));

		Assert.NotNull(started.StartTime);
		Assert.Equal(1, await CycleCountAsync());
	}

	[Fact]
	public async Task Start_new_when_one_is_already_active_returns_it_without_duplicating()
	{
		var first = await PolarisAsync(api => api.StartNewAsync(cancellationToken: TestContext.Current.CancellationToken));
		var second = await PolarisAsync(api => api.StartNewAsync(cancellationToken: TestContext.Current.CancellationToken));

		Assert.Equal(first.Id, second.Id);
		Assert.Equal(1, await CycleCountAsync());
	}

	[Fact]
	public async Task Planning_an_executive_on_the_current_cycle_with_none_active_starts_one()
	{
		Assert.Equal(0, await CycleCountAsync());

		var result = await PolarisAsync(api => api.PlanExecutiveAsync(
			new PolarisExecutivePlan(PolarisExecutivePlanningMode.OneShot, ExecutiveTitle: "Do the thing"),
			cancellationToken: TestContext.Current.CancellationToken));

		Assert.Equal(1, await CycleCountAsync());
		var cycle = await SingleCycleAsync();
		Assert.NotNull(cycle.StartTime);                         // the started cycle is active
		Assert.Equal(cycle.Id, result.Executive.PolarisCycleId); // and the executive was added to it
	}

	[Fact]
	public async Task Activating_a_cycle_supersedes_forecasts_on_or_before_its_date()
	{
		// Polaris ids are Pleiadean date stamps; the forecast-supersession cleanup parsed them as Gregorian and so was
		// silently disabled. Activating today's cycle must now drop a forecast whose target is on/before today, keeping
		// later ones.
		var today = DateOnly.FromDateTime(DateTime.Today);
		var stale = await PolarisAsync(api => api.PlanAsync(today.AddDays(-5), 3, cancellationToken: TestContext.Current.CancellationToken)); // targets today - 2
		var future = await PolarisAsync(api => api.PlanAsync(today, 3, cancellationToken: TestContext.Current.CancellationToken));             // targets today + 3

		await PolarisAsync(api => api.StartNewAsync(cancellationToken: TestContext.Current.CancellationToken));

		var remaining = await Vault.QueryAsync(context => context.PolarisCycles.AsNoTracking()
			.Select(cycle => cycle.Id).ToListAsync(TestContext.Current.CancellationToken));
		Assert.DoesNotContain(stale.Id, remaining);
		Assert.Contains(future.Id, remaining);
	}

	[Fact]
	public async Task Adding_to_the_current_cycle_uses_the_active_one_when_present()
	{
		var active = await PolarisAsync(api => api.StartNewAsync(cancellationToken: TestContext.Current.CancellationToken));

		var result = await PolarisAsync(api => api.PlanExecutiveAsync(
			new PolarisExecutivePlan(PolarisExecutivePlanningMode.OneShot, ExecutiveTitle: "On the active one"),
			cancellationToken: TestContext.Current.CancellationToken));

		Assert.Equal(active.Id, result.Executive.PolarisCycleId);
		Assert.Equal(1, await CycleCountAsync());
	}

	[Fact]
	public async Task Moving_an_executive_to_the_next_polaris_creates_tomorrows_forecast_and_carries_the_tracked_time_forward()
	{
		var ct = TestContext.Current.CancellationToken;
		var active = await PolarisAsync(api => api.StartNewAsync(cancellationToken: ct));

		var planned = await PolarisAsync(api => api.PlanExecutiveAsync(
			new PolarisExecutivePlan(PolarisExecutivePlanningMode.OneShot, ExecutiveTitle: "Carry me over", Estimation: 60),
			cancellationToken: ct));
		var executiveId = planned.Executive.Id;

		// Track 90 minutes of work without finishing it.
		await PolarisAsync(api => api.UpdateExecutiveAsync(executiveId, new ExecutiveUpdate(Elapsed: 90), ct));

		var moved = await PolarisAsync(api => api.MoveExecutiveToNextPolarisAsync(executiveId, ct));

		// A second cycle — tomorrow's forecast — now exists, and the executive lives on it rather than the active one.
		Assert.Equal(2, await CycleCountAsync());
		Assert.NotEqual(active.Id, moved.PolarisCycleId);

		var next = await Vault.QueryAsync(context => context.PolarisCycles.AsNoTracking()
			.SingleAsync(cycle => cycle.Id == moved.PolarisCycleId, ct));
		Assert.True(next.IsForecast); // planned, not started

		// The tracked work became the fresh allocation envelope; tracking restarted from zero.
		Assert.Equal(90, moved.Estimation);
		Assert.Equal(90, moved.Minimum);
		Assert.Equal(90, moved.Maximum);
		Assert.Equal(0, moved.Elapsed);
	}

	[Fact]
	public async Task Todays_cycle_is_current_while_planned_or_running_but_not_once_concluded()
	{
		var ct = TestContext.Current.CancellationToken;
		var today = DateOnly.FromDateTime(DateTime.Today);

		// Planned for today: current, so the briefing can show it planned and offer to begin it.
		var planned = await PolarisAsync(api => api.PlanAsync(today.AddDays(-1), 1, cancellationToken: ct));
		Assert.Equal(planned.Id, (await CurrentCycleAsync())?.Id);
		Assert.Equal(planned.Id, (await BriefingCycleAsync())?.Id);

		await PolarisAsync(api => api.StartNewAsync(cancellationToken: ct));
		Assert.Equal(planned.Id, (await CurrentCycleAsync())?.Id);

		// Concluded: today's cycle is no longer current, rather than coming back to be timed as if it still ran.
		await PolarisAsync(api => api.EndAsync(cancellationToken: ct));
		Assert.Null(await CurrentCycleAsync());
		Assert.Null(await BriefingCycleAsync());
		Assert.NotNull((await SingleCycleAsync()).EndTime);
	}

	private Task<PolarisCycle?> CurrentCycleAsync()
		=> PolarisAsync(api => api.GetAsync(cancellationToken: TestContext.Current.CancellationToken));

	private async Task<PolarisCycle?> BriefingCycleAsync()
	{
		var briefing = await Vault.WithScopeAsync(services => services.GetRequiredService<ISystemApi>().GetBriefingAsync(TestContext.Current.CancellationToken));
		return briefing.CurrentPolaris;
	}
}
