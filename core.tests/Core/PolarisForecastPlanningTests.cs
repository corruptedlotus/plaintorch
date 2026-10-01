using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Planning Polaris cycles day by day (the forecast surface): a day holds one cycle and knows which day it is, today
/// itself can be planned before it is begun, a plan — and only a plan — can be deleted again, and a deleted day can be
/// planned anew although its date-stamped id stays registered.
/// </summary>
public sealed class PolarisForecastPlanningTests : VaultTestBase
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

	private Task<T> PolarisAsync<T>(Func<IPolarisCycleApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IPolarisCycleApi>()));

	private Task PolarisAsync(Func<IPolarisCycleApi, Task> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IPolarisCycleApi>()));

	private Task<PolarisCycle> PlanAsync(int daysAhead)
		=> PolarisAsync(api => api.PlanAsync(Today, daysAhead, cancellationToken: Token));

	private Task<bool> CycleExistsAsync(string id)
		=> Vault.QueryAsync(context => context.PolarisCycles.AnyAsync(cycle => cycle.Id == id, Token));

	private async Task<PolarisExecutivePlanResult> PlanObjectiveAsync(string title, string cycleId)
	{
		var objective = await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.CreateStandaloneAsync(title, cancellationToken: Token));
		return await PolarisAsync(api => api.PlanExecutiveAsync(
			new PolarisExecutivePlan(PolarisExecutivePlanningMode.FromObjective, ObjectiveId: objective.Id), cycleId, Token));
	}

	[Fact]
	public async Task A_cycle_knows_its_day()
	{
		var planned = await PlanAsync(3);

		Assert.Equal(Today.AddDays(3), planned.Day);
		Assert.True(planned.IsForecast);
	}

	[Fact]
	public async Task Today_itself_can_be_planned_and_beginning_the_day_activates_that_plan()
	{
		var planned = await PlanAsync(0);
		Assert.True(planned.IsForecast);
		Assert.Equal(Today, planned.Day);

		var started = await PolarisAsync(api => api.StartNewAsync(cancellationToken: Token));

		// The day's plan is begun in place, not duplicated: its id IS the day.
		Assert.Equal(planned.Id, started.Id);
		Assert.NotNull(started.StartTime);
		Assert.Equal(1, await Vault.QueryAsync(context => context.PolarisCycles.CountAsync(Token)));
	}

	[Fact]
	public async Task A_forecast_cannot_be_planned_for_a_day_before_its_reference()
	{
		var registered = await Vault.QueryAsync(context => context.PuckRegistryEntries.CountAsync(Token));

		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => PlanAsync(-1));

		// Refused before an id was minted for the day.
		Assert.Equal(registered, await Vault.QueryAsync(context => context.PuckRegistryEntries.CountAsync(Token)));
	}

	[Fact]
	public async Task A_day_that_already_has_a_cycle_cannot_be_planned_again()
	{
		var planned = await PlanAsync(2);

		// The same day, reached from another reference.
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			PolarisAsync(api => api.PlanAsync(Today.AddDays(1), 1, cancellationToken: Token)));

		Assert.Equal([planned.Id], await Vault.QueryAsync(context => context.PolarisCycles.Select(cycle => cycle.Id).ToListAsync(Token)));
	}

	[Fact]
	public async Task Deleting_a_forecast_removes_it_with_its_plan_and_keeps_the_objectives_behind_it()
	{
		var forecast = await PlanAsync(1);
		var planned = await PlanObjectiveAsync("Scout the pass", forecast.Id);

		await PolarisAsync(api => api.DeleteAsync(forecast.Id, Token));

		Assert.False(await CycleExistsAsync(forecast.Id));
		Assert.False(await Vault.QueryAsync(context => context.Set<Executive>().AnyAsync(item => item.Id == planned.Executive.Id, Token)));
		var objective = await Vault.QueryAsync(context => context.Objectives.AsNoTracking().FirstAsync(item => item.Id == planned.Objective!.Id, Token));
		// Deleting the plan never walks state back: planning promoted the objective, and it stays promoted.
		Assert.Equal(ObjectiveStatus.Polaris, objective.Status);
	}

	[Fact]
	public async Task A_begun_cycle_cannot_be_deleted()
	{
		var active = await PolarisAsync(api => api.StartNewAsync(cancellationToken: Token));

		await Assert.ThrowsAsync<InvalidOperationException>(() => PolarisAsync(api => api.DeleteAsync(active.Id, Token)));

		Assert.True(await CycleExistsAsync(active.Id));
	}

	[Fact]
	public async Task A_deleted_forecasts_day_can_be_planned_again()
	{
		var first = await PlanAsync(1);
		await PolarisAsync(api => api.DeleteAsync(first.Id, Token));

		// The day's id stays registered with PUCK after the delete; planning the day again reuses it.
		var second = await PlanAsync(1);

		Assert.Equal(first.Id, second.Id);
		Assert.True(await CycleExistsAsync(second.Id));
	}

	[Fact]
	public async Task Moving_an_executive_to_the_next_day_after_its_forecast_was_deleted_plans_that_day_again()
	{
		var active = await PolarisAsync(api => api.StartNewAsync(cancellationToken: Token));
		var planned = await PlanObjectiveAsync("Carry the torch", active.Id);
		var tomorrow = await PlanAsync(1);
		await PolarisAsync(api => api.DeleteAsync(tomorrow.Id, Token));

		var moved = await PolarisAsync(api => api.MoveExecutiveToNextPolarisAsync(planned.Executive.Id, Token));

		Assert.Equal(tomorrow.Id, moved.PolarisCycleId);
		Assert.True(await CycleExistsAsync(tomorrow.Id));
	}

	[Fact]
	public async Task Listing_by_day_returns_the_cycles_of_the_range_in_day_order()
	{
		var active = await PolarisAsync(api => api.StartNewAsync(cancellationToken: Token));
		var inThree = await PlanAsync(3);
		var tomorrow = await PlanAsync(1);
		var inTen = await PlanAsync(10);
		var planned = await PlanObjectiveAsync("Mind the gate", tomorrow.Id);

		var window = await PolarisAsync(api => api.ListByDayAsync(Today, Today.AddDays(5), Token));
		Assert.Equal([active.Id, tomorrow.Id, inThree.Id], window.Select(cycle => cycle.Id));
		Assert.Equal([Today, Today.AddDays(1), Today.AddDays(3)], window.Select(cycle => cycle.Day!.Value));
		// Served with its executives, and each executive with the incentive behind it.
		var executive = Assert.Single(window[1].Executives);
		Assert.Equal(planned.Executive.Id, executive.Id);
		Assert.Equal(planned.Objective!.Id, executive.Incentive?.Id);

		var onward = await PolarisAsync(api => api.ListByDayAsync(Today.AddDays(2), cancellationToken: Token));
		Assert.Equal([inThree.Id, inTen.Id], onward.Select(cycle => cycle.Id));
	}
}
