using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Enforcement;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Sprints and cycles once they have run, and the work they hold (PEP103 patch 5, user rulings): what an ended sprint or
/// cycle holds is history, a sprint never begins on a day still to come, an executive works at an objective or a started
/// decree whether it is added or reassigned, its reward follows it wherever it goes, and an API write that changes what a
/// note shows queues the note's rewrite.
/// </summary>
public sealed class PastCycleTests : VaultTestBase
{
	private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

	private CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> Polaris<T>(Func<IPolarisCycleApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IPolarisCycleApi>()));
	private Task Polaris(Func<IPolarisCycleApi, Task> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IPolarisCycleApi>()));
	private Task<T> Onrush<T>(Func<IOnrushSprintApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IOnrushSprintApi>()));
	private Task Onrush(Func<IOnrushSprintApi, Task> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IOnrushSprintApi>()));
	private Task<T> Objective<T>(Func<IObjectiveApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IObjectiveApi>()));
	private Task<T> Declarative<T>(Func<IDeclarativeApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDeclarativeApi>()));
	private Task<T> Deps<T>(Func<IDependencyApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDependencyApi>()));

	private static async Task<string> RefusedRuleAsync(Func<Task> action)
	{
		var refused = await Assert.ThrowsAsync<StateEnforcementException>(action);
		Assert.Equal(StateDecisionOutcome.Refuse, refused.Decision.Outcome);
		return Assert.Single(refused.Decision.Conflicts).Rule;
	}

	private Task<Executive> ExecutiveAsync(long id)
		=> Vault.QueryAsync(context => context.Set<Executive>().AsNoTracking().IgnoreAutoIncludes().SingleAsync(item => item.Id == id, Ct));

	private Task<List<CelestronTransaction>> GrantsAsync(string decreeId)
		=> Vault.QueryAsync(context => context.CelestronLedger.AsNoTracking().Where(item => item.SourcePuck == decreeId).ToListAsync(Ct));

	[Fact]
	public async Task An_ended_cycles_work_can_no_longer_change()
	{
		var cycle = await Polaris(api => api.StartNewAsync(cancellationToken: Ct));
		var objective = await Objective(api => api.CreateStandaloneAsync("Worked", cancellationToken: Ct));
		var worked = await Polaris(api => api.PlanExecutiveAsync(new PolarisExecutivePlan(PolarisExecutivePlanningMode.FromObjective, ObjectiveId: objective.Id), cycle.Id, Ct));
		var later = await Polaris(api => api.PlanAsync(Today, 1, cancellationToken: Ct));
		await Polaris(api => api.EndAsync(cycle.Id, cancellationToken: Ct));

		Assert.Equal(StateJudge.CycleEndedRule, await RefusedRuleAsync(() => Polaris(api => api.UpdateExecutiveAsync(worked.Executive.Id, new ExecutiveUpdate(Executed: true), Ct))));
		Assert.Equal(StateJudge.CycleEndedRule, await RefusedRuleAsync(() => Polaris(api => api.UpdateExecutiveAsync(worked.Executive.Id, new ExecutiveUpdate(MoveToPolarisCycleId: later.Id), Ct))));
		Assert.Equal(StateJudge.CycleEndedRule, await RefusedRuleAsync(() => Polaris(api => api.RemoveExecutiveAsync(worked.Executive.Id, Ct))));
		Assert.Equal(StateJudge.CycleEndedRule, await RefusedRuleAsync(() =>
			Polaris(api => api.PlanExecutiveAsync(new PolarisExecutivePlan(PolarisExecutivePlanningMode.OneShot, Title: "Late entry"), cycle.Id, Ct))));

		var kept = await ExecutiveAsync(worked.Executive.Id);
		Assert.False(kept.Executed);
		Assert.Equal(cycle.Id, kept.PolarisCycleId);

		// Deleting the objective keeps the record, with its reference cleared: that is no change to the cycle's work.
		await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>().DeleteAsync(objective.Id, Ct));
		Assert.Null((await ExecutiveAsync(worked.Executive.Id)).IncentiveId);
	}

	[Fact]
	public async Task An_ended_sprint_keeps_its_orders_and_takes_nothing_in()
	{
		var sprint = await Onrush(api => api.StartNewAsync(cancellationToken: Ct));
		var order = await Onrush(api => api.IssueExecutiveOrderAsync(sprint.Id, new ExecutiveOrderPlan("Hold the line"), Ct));
		var member = await Objective(api => api.CreateStandaloneAsync("Member", cancellationToken: Ct));
		await Objective(api => api.AddToOnrushAsync(member.Id, sprint.Id, Ct));
		await Onrush(api => api.EndAsync(sprint.Id, cancellationToken: Ct));

		Assert.Equal(StateJudge.SprintEndedRule, await RefusedRuleAsync(() => Onrush(api => api.UpdateExecutiveOrderAsync(order.Id, new ExecutiveOrderUpdate(Title: "Retreat"), Ct))));
		Assert.Equal(StateJudge.SprintEndedRule, await RefusedRuleAsync(() => Onrush(api => api.DeleteExecutiveOrderAsync(order.Id, Ct))));
		Assert.Equal(StateJudge.SprintEndedRule, await RefusedRuleAsync(() => Onrush(api => api.IssueExecutiveOrderAsync(sprint.Id, new ExecutiveOrderPlan("Too late"), Ct))));
		Assert.Equal(StateJudge.SprintEndedRule, await RefusedRuleAsync(() => Deps(api => api.CreateCheckpointAsync("Late gate", onrushSprintId: sprint.Id, cancellationToken: Ct))));
		var latecomer = await Objective(api => api.CreateStandaloneAsync("Latecomer", cancellationToken: Ct));
		Assert.Equal(StateJudge.SprintEndedRule, await RefusedRuleAsync(() => Objective(api => api.AddToOnrushAsync(latecomer.Id, sprint.Id, Ct))));

		// Work may leave it for the next sprint, and the sprint itself may still be deleted.
		await Objective(api => api.RemoveFromOnrushAsync(member.Id, Ct));
		await Onrush(api => api.DeleteAsync(sprint.Id, Ct));
		Assert.False(await Vault.QueryAsync(context => context.OnrushSprints.AnyAsync(item => item.Id == sprint.Id, Ct)));
	}

	[Fact]
	public async Task A_sprint_never_begins_on_a_day_still_to_come()
	{
		Assert.Equal(StateJudge.SprintEarlyStartRule, await RefusedRuleAsync(() => Onrush(api => api.StartNewAsync(Today.AddDays(2), Ct))));

		// A plan dated ahead is begun today when started now.
		await Onrush(api => api.PlanAsync(new OnrushSprintPlan("Next sprint", StartDate: Today.AddDays(5)), Ct));
		var begun = await Onrush(api => api.StartNewAsync(cancellationToken: Ct));
		Assert.NotEqual(OnrushSprint.PlanningPlaceholderId, begun.Id);
		Assert.Equal(Today, begun.StartDate);
	}

	[Fact]
	public async Task An_executive_works_at_an_objective_or_a_started_decree()
	{
		var cycle = await Polaris(api => api.StartNewAsync(cancellationToken: Ct));
		var routine = await Declarative(api => api.CreateDecreeAsync(new DecreePlan("Routine"), Ct));
		var dropped = await Declarative(api => api.CreateDecreeAsync(new DecreePlan("Dropped"), Ct));
		await Declarative(api => api.UpdateDecreeAsync(dropped.Id, new DecreeUpdate(Status: DecreeStatus.Abandoned), Ct));
		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Solstice", Date: Today.AddDays(30)), Ct));
		var finished = await Objective(api => api.CreateStandaloneAsync("Finished", cancellationToken: Ct));
		await Objective(api => api.ShiftWorkflowAsync(finished.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Done), Ct));

		Assert.Equal(StateJudge.IncentiveRule, await RefusedRuleAsync(() => Polaris(api => api.AddDecreeExecutiveAsync(new PolarisDecreeAdd(dropped.Id), cycle.Id, Ct))));

		// Reassigning follows the same rules as adding.
		var executive = await Polaris(api => api.AddDecreeExecutiveAsync(new PolarisDecreeAdd(routine.Id), cycle.Id, Ct));
		Assert.Equal(StateJudge.IncentiveRule, await RefusedRuleAsync(() => Polaris(api => api.UpdateExecutiveAsync(executive.Id, new ExecutiveUpdate(ObjectiveId: fate.Id), Ct))));
		Assert.Equal(StateJudge.IncentiveRule, await RefusedRuleAsync(() => Polaris(api => api.UpdateExecutiveAsync(executive.Id, new ExecutiveUpdate(ObjectiveId: dropped.Id), Ct))));

		// A finished objective may be worked, as it is.
		await Polaris(api => api.UpdateExecutiveAsync(executive.Id, new ExecutiveUpdate(ObjectiveId: finished.Id), Ct));
		Assert.Equal(finished.Id, (await ExecutiveAsync(executive.Id)).IncentiveId);
		Assert.Equal(ObjectiveStatus.Done, await Vault.QueryAsync(context => context.Objectives.AsNoTracking().IgnoreAutoIncludes()
			.Where(item => item.Id == finished.Id).Select(item => item.Status).SingleAsync(Ct)));
	}

	[Fact]
	public async Task A_decree_executives_reward_follows_it()
	{
		var cycle = await Polaris(api => api.StartNewAsync(cancellationToken: Ct));
		var tomorrow = await Polaris(api => api.PlanAsync(Today, 1, cancellationToken: Ct));
		var routine = await Declarative(api => api.CreateDecreeAsync(new DecreePlan("Routine", ActiveCelestron: 5), Ct));
		var stretch = await Declarative(api => api.CreateDecreeAsync(new DecreePlan("Stretch", ActiveCelestron: 3), Ct));
		var executive = await Polaris(api => api.AddDecreeExecutiveAsync(new PolarisDecreeAdd(routine.Id), cycle.Id, Ct));
		await Polaris(api => api.UpdateExecutiveAsync(executive.Id, new ExecutiveUpdate(Executed: true), Ct));
		Assert.Contains(cycle.Id, Assert.Single(await GrantsAsync(routine.Id)).Description);

		// Moved on executed, it takes its grant along; un-executed there, it gives it back; executed again, it is paid once.
		await Polaris(api => api.UpdateExecutiveAsync(executive.Id, new ExecutiveUpdate(MoveToPolarisCycleId: tomorrow.Id), Ct));
		Assert.Contains(tomorrow.Id, Assert.Single(await GrantsAsync(routine.Id)).Description);
		await Polaris(api => api.UpdateExecutiveAsync(executive.Id, new ExecutiveUpdate(Executed: false), Ct));
		Assert.Empty(await GrantsAsync(routine.Id));
		await Polaris(api => api.UpdateExecutiveAsync(executive.Id, new ExecutiveUpdate(Executed: true), Ct));
		Assert.Single(await GrantsAsync(routine.Id));

		// Reassigned executed, it gives back the old decree's grant and earns the new one's.
		await Polaris(api => api.UpdateExecutiveAsync(executive.Id, new ExecutiveUpdate(ObjectiveId: stretch.Id), Ct));
		Assert.Empty(await GrantsAsync(routine.Id));
		Assert.Equal(3, Assert.Single(await GrantsAsync(stretch.Id)).Amount);
	}

	[Fact]
	public async Task Assign_all_queues_the_notes_it_changes()
	{
		var sprint = await Onrush(api => api.StartNewAsync(cancellationToken: Ct));
		var loose = await Objective(api => api.CreateStandaloneAsync("Loose work", cancellationToken: Ct));
		await Vault.BeginObjectiveBoundaryAsync(loose.Id);
		await Objective(api => api.ShiftWorkflowAsync(loose.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Onboard), Ct));

		await Onrush(api => api.AssignAllOnboardObjectivesToSelfAsync(sprint.Id, Ct));

		Assert.Contains($"onrush: {sprint.Id}", Vault.ReadVaultFile("Objectives/Loose work.md"));
		Assert.Empty(await Vault.QueryAsync(context => context.VaultWriteIntents.AsNoTracking().ToListAsync(Ct)));
	}
}
