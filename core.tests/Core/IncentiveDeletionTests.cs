using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Diagnostics;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Deleting a backlog item keeps the work recorded against it. An objective or a decree deleted through any pathway (the
/// API, or the watcher after its note is deleted) keeps its executives in ended Polaris cycles as work records with the
/// reference cleared, and loses its executives in the active cycle and in planned or forecast cycles, which could no
/// longer be worked. Executives no longer block the delete: before this, the objective API refused it, a planned decree's
/// delete failed on the foreign key, and a deleted note raised a standing <c>delete-blocked</c> error.
/// </summary>
public sealed class IncentiveDeletionTests : VaultTestBase
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private Task<T> Polaris<T>(Func<IPolarisCycleApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IPolarisCycleApi>()));

	/// <summary>
	/// Plans one incentive into an ended cycle (worked: executed, 42 minutes elapsed), the active cycle, and a forecast
	/// cycle, and returns the three executive ids in that order. <paramref name="plan"/> adds the incentive to a cycle.
	/// </summary>
	private async Task<(long Ended, long Active, long Forecast)> PlanAcrossCyclesAsync(Func<string?, Task<Executive>> plan)
	{
		var past = await Polaris(api => api.StartNewAsync(DateTimeOffset.Now.AddDays(-3), cancellationToken: Token));
		var ended = await plan(past.Id);
		await Polaris(api => api.EndAsync(past.Id, DateTimeOffset.Now.AddDays(-3).AddHours(2), Token));
		await Vault.QueryAsync(context => context.Set<Executive>()
			.Where(item => item.Id == ended.Id)
			.ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Executed, true).SetProperty(item => item.Elapsed, 42), Token));

		var active = await plan(null);
		var forecastCycle = await Polaris(api => api.PlanAsync(DateOnly.FromDateTime(DateTime.Today), 2, cancellationToken: Token));
		var forecast = await plan(forecastCycle.Id);
		return (ended.Id, active.Id, forecast.Id);
	}

	private async Task<(long Ended, long Active, long Forecast)> PlanObjectiveAcrossCyclesAsync(string objectiveId)
		=> await PlanAcrossCyclesAsync(async cycleId => (await Polaris(api => api.PlanExecutiveAsync(
			new PolarisExecutivePlan(PolarisExecutivePlanningMode.FromObjective, ObjectiveId: objectiveId),
			cycleId,
			Token))).Executive);

	private Task<Executive?> ExecutiveAsync(long id)
		=> Vault.QueryAsync(context => context.Set<Executive>().AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(item => item.Id == id, Token));

	private Task<bool> IncentiveExistsAsync(string id)
		=> Vault.QueryAsync(context => context.Incentives.AnyAsync(item => item.Id == id, Token));

	private async Task AssertReleasedAsync(string incentiveId, (long Ended, long Active, long Forecast) executives)
	{
		Assert.False(await IncentiveExistsAsync(incentiveId));

		// The ended cycle's work record stays, detached from the deleted item, with its work intact.
		var ended = await ExecutiveAsync(executives.Ended);
		Assert.NotNull(ended);
		Assert.Null(ended.IncentiveId);
		Assert.True(ended.Executed);
		Assert.Equal(42, ended.Elapsed);

		// The plans that can no longer be worked go with it.
		Assert.Null(await ExecutiveAsync(executives.Active));
		Assert.Null(await ExecutiveAsync(executives.Forecast));
	}

	[Fact]
	public async Task Deleting_an_objective_keeps_its_ended_work_and_drops_its_live_plans()
	{
		var objective = await Vault.SeedStandaloneObjectiveAsync("Planned");
		var executives = await PlanObjectiveAcrossCyclesAsync(objective.Id);

		await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>().DeleteAsync(objective.Id, Token));

		await AssertReleasedAsync(objective.Id, executives);
	}

	[Fact]
	public async Task Deleting_a_planned_objectives_note_deletes_it_the_same_way_with_no_blocked_delete()
	{
		var objective = await Vault.SeedStandaloneObjectiveAsync("Noted");
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);
		var executives = await PlanObjectiveAcrossCyclesAsync(objective.Id);
		var note = Vault.AbsolutePath("Objectives/Noted.md");
		File.Delete(note);

		await Vault.ReconcileWithIssuesAsync(note);

		await AssertReleasedAsync(objective.Id, executives);
		Assert.DoesNotContain(
			Vault.GetSingleton<OperationStatusRegistry>().GetActiveStatuses(),
			status => string.Equals(status.ScopeKey, note, StringComparison.OrdinalIgnoreCase));
		Assert.Equal(1, await Vault.QueryAsync(context => context.DatabaseGraveyardEntries.CountAsync(item => item.EntityId == objective.Id, Token)));
	}

	[Fact]
	public async Task Deleting_a_planned_decree_keeps_its_ended_work_and_drops_its_live_plans()
	{
		var decree = await Vault.WithScopeAsync(services => services.GetRequiredService<IDeclarativeApi>()
			.CreateDecreeAsync(new DecreePlan("Routine"), Token));
		var executives = await PlanAcrossCyclesAsync(cycleId => Polaris(api => api.AddDecreeExecutiveAsync(new PolarisDecreeAdd(decree.Id), cycleId, Token)));

		await Vault.WithScopeAsync(services => services.GetRequiredService<IDeclarativeApi>().DeleteDecreeAsync(decree.Id, Token));

		await AssertReleasedAsync(decree.Id, executives);
	}
}
