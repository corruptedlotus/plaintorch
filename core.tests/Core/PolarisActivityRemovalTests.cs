using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Taking an activity back out of a Polaris cycle deletes the cycle's record of it and nothing else. The core moves
/// backlog state forward on cycle participation and never back: the objective behind a removed executive keeps its
/// state, the decree behind a removed attentive stays, and what a done attentive granted stays granted.
/// </summary>
public sealed class PolarisActivityRemovalTests : VaultTestBase
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private Task<T> PolarisAsync<T>(Func<IPolarisCycleApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IPolarisCycleApi>()));

	private Task RemoveAsync(Func<IPolarisCycleApi, Task> action)
		=> Vault.WithScopeAsync(async services =>
		{
			await action(services.GetRequiredService<IPolarisCycleApi>());
			return true;
		});

	private async Task<PolarisExecutivePlanResult> PlanObjectiveAsync(string title)
	{
		var objective = await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.CreateStandaloneAsync(title, cancellationToken: Token));
		return await PolarisAsync(api => api.PlanExecutiveAsync(
			new PolarisExecutivePlan(PolarisExecutivePlanningMode.FromObjective, ObjectiveId: objective.Id), null, Token));
	}

	[Fact]
	public async Task Removing_an_executive_deletes_it_and_keeps_its_objective()
	{
		var planned = await PlanObjectiveAsync("Take the bridge");

		await RemoveAsync(api => api.RemoveExecutiveAsync(planned.Executive.Id, Token));

		Assert.False(await Vault.QueryAsync(context => context.Set<Executive>().AnyAsync(item => item.Id == planned.Executive.Id, Token)));
		var objective = await Vault.QueryAsync(context => context.Objectives.AsNoTracking().FirstAsync(item => item.Id == planned.Objective!.Id, Token));
		// Removal never walks state back: planning promoted the objective, and it stays promoted.
		Assert.Equal(ObjectiveStatus.Polaris, objective.Status);
	}

	[Fact]
	public async Task An_objective_can_be_planned_again_after_its_executive_is_removed()
	{
		var planned = await PlanObjectiveAsync("Second thoughts");
		await RemoveAsync(api => api.RemoveExecutiveAsync(planned.Executive.Id, Token));

		var again = await PolarisAsync(api => api.PlanExecutiveAsync(
			new PolarisExecutivePlan(PolarisExecutivePlanningMode.FromObjective, ObjectiveId: planned.Objective!.Id), null, Token));

		Assert.NotEqual(planned.Executive.Id, again.Executive.Id);
	}

	[Fact]
	public async Task Removing_an_attentive_deletes_it_and_keeps_its_decree()
	{
		var decree = await Vault.WithScopeAsync(services => services.GetRequiredService<IDeclarativeApi>()
			.CreateDecreeAsync(new DecreePlan("Stand watch"), Token));
		var attentive = await PolarisAsync(api => api.AddDecreeAttentiveAsync(new PolarisAttentiveAdd(decree.Id), null, Token));

		await RemoveAsync(api => api.RemoveAttentiveAsync(attentive.Id, Token));

		Assert.False(await Vault.QueryAsync(context => context.Attentives.AnyAsync(item => item.Id == attentive.Id, Token)));
		Assert.True(await Vault.QueryAsync(context => context.Decrees.AnyAsync(item => item.Id == decree.Id, Token)));
	}

	[Fact]
	public async Task What_a_done_attentive_granted_stays_granted()
	{
		var decree = await Vault.WithScopeAsync(services => services.GetRequiredService<IDeclarativeApi>()
			.CreateDecreeAsync(new DecreePlan("Stand watch", ActiveCelestron: 5), Token));
		var attentive = await PolarisAsync(api => api.AddDecreeAttentiveAsync(new PolarisAttentiveAdd(decree.Id), null, Token));
		await Vault.WithScopeAsync(services => services.GetRequiredService<IDeclarativeApi>()
			.UpdateAttentiveAsync(new AttentiveOccurrenceRef(Id: attentive.Id), new AttentiveUpdate(Resolution: AttentiveResolution.Done), Token));
		var granted = await Vault.QueryAsync(context => context.CelestronLedger.CountAsync(item => item.SourcePuck == decree.Id, Token));

		await RemoveAsync(api => api.RemoveAttentiveAsync(attentive.Id, Token));

		Assert.True(granted > 0);
		Assert.Equal(granted, await Vault.QueryAsync(context => context.CelestronLedger.CountAsync(item => item.SourcePuck == decree.Id, Token)));
	}

	[Fact]
	public async Task Removing_what_is_not_there_is_refused()
	{
		await Assert.ThrowsAsync<InvalidOperationException>(() => RemoveAsync(api => api.RemoveExecutiveAsync(987654, Token)));
		await Assert.ThrowsAsync<InvalidOperationException>(() => RemoveAsync(api => api.RemoveAttentiveAsync(987654, Token)));
	}
}
