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
/// How sprints and cycles run (PEP103 patch 4): neither ends twice nor before it begins, an ended one never begins again, a
/// Polaris cycle never begins before its day, work moved on lands in the next cycle that can still begin, starting a sprint
/// uses up the plan or keeps the running one, and joining the active or planning sprint creates it when it is missing.
/// </summary>
public sealed class CycleRunTests : VaultTestBase
{
	private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

	private CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> Polaris<T>(Func<IPolarisCycleApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IPolarisCycleApi>()));
	private Task<T> Onrush<T>(Func<IOnrushSprintApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IOnrushSprintApi>()));
	private Task<T> Objective<T>(Func<IObjectiveApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IObjectiveApi>()));
	private Task<T> Directive<T>(Func<IDirectiveApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDirectiveApi>()));

	private static async Task<string> RefusedRuleAsync(Func<Task> action)
	{
		var refused = await Assert.ThrowsAsync<StateEnforcementException>(action);
		Assert.Equal(StateDecisionOutcome.Refuse, refused.Decision.Outcome);
		return Assert.Single(refused.Decision.Conflicts).Rule;
	}

	private Task<PolarisCycle> CycleAsync(string id)
		=> Vault.QueryAsync(context => context.PolarisCycles.AsNoTracking().SingleAsync(cycle => cycle.Id == id, Ct));

	private Task<OnrushSprint> SprintAsync(string id)
		=> Vault.QueryAsync(context => context.OnrushSprints.AsNoTracking().IgnoreAutoIncludes().SingleAsync(sprint => sprint.Id == id, Ct));

	[Fact]
	public async Task A_polaris_cycle_ends_once_and_never_begins_again()
	{
		var cycle = await Polaris(api => api.StartNewAsync(cancellationToken: Ct));
		var ended = await Polaris(api => api.EndAsync(cycle.Id, cancellationToken: Ct));

		Assert.Equal(StateJudge.CycleRestartRule, await RefusedRuleAsync(() => Polaris(api => api.BeginAsync(cycle.Id, cancellationToken: Ct))));
		Assert.Equal(StateJudge.CycleRestartRule, await RefusedRuleAsync(() => Polaris(api => api.EndAsync(cycle.Id, ended.EndTime!.Value.AddHours(1), Ct))));

		var stored = await CycleAsync(cycle.Id);
		Assert.Equal(ended.StartTime, stored.StartTime);
		Assert.Equal(ended.EndTime, stored.EndTime);
	}

	[Fact]
	public async Task A_polaris_cycle_neither_ends_before_it_begins_nor_begins_before_its_day()
	{
		var tomorrow = await Polaris(api => api.PlanAsync(Today, 1, cancellationToken: Ct));

		Assert.Equal(StateJudge.CycleEndRule, await RefusedRuleAsync(() => Polaris(api => api.EndAsync(tomorrow.Id, cancellationToken: Ct))));
		Assert.Equal(StateJudge.CycleEarlyStartRule, await RefusedRuleAsync(() => Polaris(api => api.BeginAsync(tomorrow.Id, cancellationToken: Ct))));

		var stored = await CycleAsync(tomorrow.Id);
		Assert.Null(stored.StartTime);
		Assert.Null(stored.EndTime);
	}

	[Fact]
	public async Task A_sprint_ends_once_after_it_begins_and_never_begins_again()
	{
		var sprint = await Onrush(api => api.StartNewAsync(Today.AddDays(-5), Ct));
		Assert.Equal(StateJudge.SprintEndRule, await RefusedRuleAsync(() => Onrush(api => api.EndAsync(sprint.Id, Today.AddDays(-6), Ct))));

		await Onrush(api => api.EndAsync(sprint.Id, Today, Ct));

		Assert.Equal(StateJudge.SprintRestartRule, await RefusedRuleAsync(() => Onrush(api => api.EndAsync(sprint.Id, cancellationToken: Ct))));
		Assert.Equal(StateJudge.SprintRestartRule, await RefusedRuleAsync(() => Onrush(api => api.BeginAsync(sprint.Id, cancellationToken: Ct))));
		Assert.Equal(StateJudge.SprintRestartRule, await RefusedRuleAsync(() =>
			Onrush(api => api.UpdateAsync(sprint.Id, new OnrushSprintUpdate(StartDate: new Optional<DateOnly?>(Today)), Ct))));

		// Clearing the end takes it up again.
		await Onrush(api => api.UpdateAsync(sprint.Id, new OnrushSprintUpdate(EndDate: new Optional<DateOnly?>(null)), Ct));
		Assert.Null((await SprintAsync(sprint.Id)).EndDate);
	}

	[Fact]
	public async Task The_planning_sprint_never_ends()
	{
		await Onrush(api => api.PlanAsync(new OnrushSprintPlan("Next sprint"), Ct));

		Assert.Equal(StateJudge.SprintEndRule, await RefusedRuleAsync(() => Onrush(api => api.EndAsync(OnrushSprint.PlanningPlaceholderId, cancellationToken: Ct))));
		Assert.Equal(StateJudge.SprintEndRule, await RefusedRuleAsync(() =>
			Onrush(api => api.UpdateAsync(OnrushSprint.PlanningPlaceholderId, new OnrushSprintUpdate(EndDate: new Optional<DateOnly?>(Today.AddDays(7))), Ct))));
	}

	[Fact]
	public async Task Work_moved_on_from_an_old_cycle_lands_in_the_next_cycle_that_can_still_begin()
	{
		// Three days ago: planned, begun on its day, ended, with work left in it.
		var past = await Polaris(api => api.PlanAsync(Today.AddDays(-3), 0, cancellationToken: Ct));
		var start = new DateTimeOffset(Today.AddDays(-3).ToDateTime(new TimeOnly(9, 0)));
		await Polaris(api => api.BeginAsync(past.Id, start, Ct));
		await Polaris(api => api.EndAsync(past.Id, start.AddHours(8), Ct));
		var objective = await Objective(api => api.CreateStandaloneAsync("Carried work", cancellationToken: Ct));
		var planned = await Polaris(api => api.PlanExecutiveAsync(new PolarisExecutivePlan(PolarisExecutivePlanningMode.FromObjective, ObjectiveId: objective.Id), past.Id, Ct));

		// Today has not begun: the work lands in today's cycle, not in the past one after the old.
		var moved = await Polaris(api => api.MoveExecutiveToNextPolarisAsync(planned.Executive.Id, Ct));
		Assert.Equal(Today, (await CycleAsync(moved.PolarisCycleId)).Day);

		// Once today has begun, the next cycle that can begin is tomorrow's.
		await Polaris(api => api.StartNewAsync(cancellationToken: Ct));
		var again = await Polaris(api => api.MoveExecutiveToNextPolarisAsync(planned.Executive.Id, Ct));
		Assert.Equal(Today.AddDays(1), (await CycleAsync(again.PolarisCycleId)).Day);
	}

	[Fact]
	public async Task Starting_a_sprint_uses_up_the_plan_and_keeps_a_running_one()
	{
		await Onrush(api => api.PlanAsync(new OnrushSprintPlan("Next sprint"), Ct));
		var member = await Objective(api => api.CreateStandaloneAsync("Planned work", cancellationToken: Ct));
		await Objective(api => api.AddToOnrushAsync(member.Id, OnrushSprint.PlanningPlaceholderId, Ct));

		var started = await Onrush(api => api.StartNewAsync(cancellationToken: Ct));

		Assert.NotEqual(OnrushSprint.PlanningPlaceholderId, started.Id);
		Assert.Equal("Next sprint", started.Title);
		Assert.False(await Vault.QueryAsync(context => context.OnrushSprints.AnyAsync(sprint => sprint.Id == OnrushSprint.PlanningPlaceholderId, Ct)));
		var joined = await Vault.QueryAsync(context => context.Objectives.AsNoTracking().IgnoreAutoIncludes().SingleAsync(item => item.Id == member.Id, Ct));
		Assert.Equal(started.Id, joined.OnrushSprintId);
		Assert.Equal(ObjectiveStatus.Onboard, joined.Status);

		var again = await Onrush(api => api.StartNewAsync(cancellationToken: Ct));
		Assert.Equal(started.Id, again.Id);
		Assert.Equal(1, await Vault.QueryAsync(context => context.OnrushSprints.CountAsync(Ct)));
	}

	[Fact]
	public async Task Joining_the_active_or_planning_sprint_creates_it_when_missing()
	{
		var planned = await Objective(api => api.CreateStandaloneAsync("Planned work", cancellationToken: Ct));
		await Objective(api => api.AddToOnrushAsync(planned.Id, OnrushSlot.Planning, Ct));
		var plan = await SprintAsync(OnrushSprint.PlanningPlaceholderId);
		Assert.Equal("Planned Onrush", plan.Title);
		Assert.Equal(ObjectiveStatus.Standby, (await Vault.QueryAsync(context => context.Objectives.AsNoTracking().IgnoreAutoIncludes().SingleAsync(item => item.Id == planned.Id, Ct))).Status);

		// The active slot begins the plan, bringing what it held on board with what joins.
		var urgent = await Objective(api => api.CreateStandaloneAsync("Urgent work", cancellationToken: Ct));
		var joined = await Objective(api => api.AddToOnrushAsync(urgent.Id, OnrushSlot.Active, Ct));
		Assert.NotEqual(OnrushSprint.PlanningPlaceholderId, joined.OnrushSprintId);
		var members = await Vault.QueryAsync(context => context.Objectives.AsNoTracking().IgnoreAutoIncludes()
			.Where(item => item.OnrushSprintId == joined.OnrushSprintId).Select(item => item.Status).ToListAsync(Ct));
		Assert.Equal([ObjectiveStatus.Onboard, ObjectiveStatus.Onboard], members);

		var directive = await Directive(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Ct));
		var member = await Directive(api => api.AddToOnrushAsync(directive.Id, OnrushSlot.Active, Ct));
		Assert.Equal(joined.OnrushSprintId, member.OnrushSprintId);
	}
}
