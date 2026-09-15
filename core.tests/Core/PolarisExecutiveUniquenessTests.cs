using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// A Polaris cycle holds at most one executive per objective. Every path that could produce a second one — planning
/// the objective again, reassigning another executive onto it — is refused at the service, and the unique index on
/// <c>(PolarisCycleId, ObjectiveId)</c> backs it at the database. One-shot executives carry no objective and are exempt.
/// </summary>
public sealed class PolarisExecutiveUniquenessTests : VaultTestBase
{
	private Task<T> PolarisAsync<T>(Func<IPolarisCycleApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IPolarisCycleApi>()));

	private Task<Objective> CreateObjectiveAsync(string title)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.CreateStandaloneAsync(title, cancellationToken: TestContext.Current.CancellationToken));

	private Task<PolarisExecutivePlanResult> PlanAsync(string objectiveId, string? cycleId = null)
		=> PolarisAsync(api => api.PlanExecutiveAsync(
			new PolarisExecutivePlan(PolarisExecutivePlanningMode.FromObjective, ObjectiveId: objectiveId),
			cycleId,
			TestContext.Current.CancellationToken));

	private Task<int> ExecutiveCountAsync(string objectiveId)
		=> Vault.QueryAsync(context => context.Set<Executive>()
			.CountAsync(item => item.ObjectiveId == objectiveId, TestContext.Current.CancellationToken));

	[Fact]
	public async Task Planning_an_objective_already_in_the_cycle_is_refused()
	{
		var objective = await CreateObjectiveAsync("Once only");
		await PlanAsync(objective.Id);

		var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => PlanAsync(objective.Id));

		Assert.Contains(objective.Id, refusal.Message);
		Assert.Equal(1, await ExecutiveCountAsync(objective.Id));
	}

	[Fact]
	public async Task The_same_objective_may_be_planned_into_different_cycles()
	{
		var objective = await CreateObjectiveAsync("Across days");
		var today = DateOnly.FromDateTime(DateTime.Today);
		var current = await PolarisAsync(api => api.StartNewAsync(cancellationToken: TestContext.Current.CancellationToken));
		var later = await PolarisAsync(api => api.PlanAsync(today, 3, cancellationToken: TestContext.Current.CancellationToken));

		await PlanAsync(objective.Id, current.Id);
		await PlanAsync(objective.Id, later.Id);

		Assert.Equal(2, await ExecutiveCountAsync(objective.Id));
	}

	[Fact]
	public async Task Reassigning_an_executive_onto_an_objective_already_in_the_cycle_is_refused()
	{
		var first = await CreateObjectiveAsync("First");
		var second = await CreateObjectiveAsync("Second");
		await PlanAsync(first.Id);
		var planned = await PlanAsync(second.Id);

		await Assert.ThrowsAsync<InvalidOperationException>(() => PolarisAsync(api => api.UpdateExecutiveAsync(
			planned.Executive.Id,
			new ExecutiveUpdate(ObjectiveId: first.Id),
			TestContext.Current.CancellationToken)));

		Assert.Equal(1, await ExecutiveCountAsync(first.Id));
		Assert.Equal(1, await ExecutiveCountAsync(second.Id));
	}

	[Fact]
	public async Task Updating_an_executive_without_changing_its_objective_still_works()
	{
		var objective = await CreateObjectiveAsync("Unchanged");
		var planned = await PlanAsync(objective.Id);

		var updated = await PolarisAsync(api => api.UpdateExecutiveAsync(
			planned.Executive.Id,
			new ExecutiveUpdate(ObjectiveId: objective.Id, Executed: true),
			TestContext.Current.CancellationToken));

		Assert.True(updated.Executed);
		Assert.Equal(objective.Id, updated.ObjectiveId);
	}

	[Fact]
	public async Task One_shot_executives_are_not_limited()
	{
		await PolarisAsync(api => api.PlanExecutiveAsync(
			new PolarisExecutivePlan(PolarisExecutivePlanningMode.OneShot, ExecutiveTitle: "Again"),
			cancellationToken: TestContext.Current.CancellationToken));
		await PolarisAsync(api => api.PlanExecutiveAsync(
			new PolarisExecutivePlan(PolarisExecutivePlanningMode.OneShot, ExecutiveTitle: "Again"),
			cancellationToken: TestContext.Current.CancellationToken));

		var count = await Vault.QueryAsync(context => context.Set<Executive>()
			.CountAsync(item => item.ObjectiveId == null, TestContext.Current.CancellationToken));
		Assert.Equal(2, count);
	}
}
