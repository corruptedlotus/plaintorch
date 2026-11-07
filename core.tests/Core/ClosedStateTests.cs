using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Enforcement;
using Pleiades.Plaintorch.Preferences;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// What is done is done (PEP103 patch 5, user rulings). A dependency added to a finished item only keeps it from closing —
/// an objective's Archived, a directive's Over — and a closed item that gains one steps back to its open finish, as the
/// enforcement level decides. A checkpoint finishes only once it has had something to wait for, keeps its finish when its
/// due passes, and takes it back when something new blocks it. Joining the active sprint or cycle pushes an objective
/// forward only, and steps an archived one back to Done.
/// </summary>
public sealed class ClosedStateTests : VaultTestBase
{
	private CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> Objective<T>(Func<IObjectiveApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IObjectiveApi>()));
	private Task<T> Deps<T>(Func<IDependencyApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDependencyApi>()));
	private Task<T> Onrush<T>(Func<IOnrushSprintApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IOnrushSprintApi>()));
	private Task<T> Polaris<T>(Func<IPolarisCycleApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IPolarisCycleApi>()));

	private Task SetEnforcementAsync(BacklogStateEnforcementLevel level)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<UserPreferenceService>().SetAsync(PreferenceKeys.BacklogStateEnforcement, level, Ct));

	private Task<ObjectiveStatus> StatusAsync(string id)
		=> Vault.QueryAsync(context => context.Objectives.AsNoTracking().IgnoreAutoIncludes()
			.Where(objective => objective.Id == id).Select(objective => objective.Status).SingleAsync(Ct));

	private Task<bool> UnlockedAsync(string id)
		=> Vault.QueryAsync(context => context.Checkpoints.AsNoTracking().Where(item => item.Id == id).Select(item => item.Unlocked).SingleAsync(Ct));

	private Task<Objective> ShiftAsync(string id, ObjectiveStatus status)
		=> Objective(api => api.ShiftWorkflowAsync(id, new ObjectiveWorkflowShift(status), Ct));

	private Task<Objective> CreateAsync(string title) => Objective(api => api.CreateStandaloneAsync(title, cancellationToken: Ct));

	private static EndpointRef ObjectiveRef(string id) => new(DependencyEndpointKind.Objective, id);

	private static EndpointRef CheckpointRef(string id) => new(DependencyEndpointKind.Checkpoint, id);

	[Fact]
	public async Task A_dependency_added_to_a_finished_objective_only_keeps_it_from_archiving()
	{
		var survey = await CreateAsync("Survey");
		var build = await CreateAsync("Build");
		await ShiftAsync(build.Id, ObjectiveStatus.Done);

		await Deps(api => api.CreateAsync(ObjectiveRef(survey.Id), ObjectiveRef(build.Id), cancellationToken: Ct));
		Assert.Equal(ObjectiveStatus.Done, await StatusAsync(build.Id));

		var refused = await Assert.ThrowsAsync<StateEnforcementException>(() => ShiftAsync(build.Id, ObjectiveStatus.Archived));
		var conflict = Assert.Single(refused.Decision.Conflicts);
		Assert.Equal(StateJudge.CloseGateRule, conflict.Rule);
		Assert.Contains("cannot be Archived: it is blocked by 1 dependency", conflict.Message);
		Assert.Equal(ObjectiveStatus.Done, await StatusAsync(build.Id));

		await ShiftAsync(survey.Id, ObjectiveStatus.Done);
		await ShiftAsync(build.Id, ObjectiveStatus.Archived);
		Assert.Equal(ObjectiveStatus.Archived, await StatusAsync(build.Id));
	}

	[Fact]
	public async Task A_closed_objective_gaining_a_dependency_steps_back_to_done()
	{
		var survey = await CreateAsync("Survey");
		var build = await CreateAsync("Build");
		await ShiftAsync(build.Id, ObjectiveStatus.Archived);

		// Under Block the dependency is refused, and nothing is stored.
		await SetEnforcementAsync(BacklogStateEnforcementLevel.Block);
		var refused = await Assert.ThrowsAsync<StateEnforcementException>(() =>
			Deps(api => api.CreateAsync(ObjectiveRef(survey.Id), ObjectiveRef(build.Id), cancellationToken: Ct)));
		Assert.Equal(StateJudge.ClosedRule, Assert.Single(refused.Decision.Conflicts).Rule);
		Assert.Contains("cannot stay Archived", refused.Message);
		Assert.Empty(await Deps(api => api.ListAsync(cancellationToken: Ct)));

		// Under Auto it goes in, and the objective keeps its finish: it is Done again, not reopened.
		await SetEnforcementAsync(BacklogStateEnforcementLevel.Auto);
		await Deps(api => api.CreateAsync(ObjectiveRef(survey.Id), ObjectiveRef(build.Id), cancellationToken: Ct));
		Assert.Equal(ObjectiveStatus.Done, await StatusAsync(build.Id));
	}

	[Fact]
	public async Task A_checkpoint_finishes_only_once_it_has_had_something_to_wait_for()
	{
		await SetEnforcementAsync(BacklogStateEnforcementLevel.Auto);
		var sprint = await Onrush(api => api.StartNewAsync(cancellationToken: Ct));
		var milestone = sprint.MilestoneCheckpointId!;

		// A new sprint's milestone waits for nothing yet, so it is not finished.
		Assert.False(await UnlockedAsync(milestone));

		// Once it waits for something, it finishes when that holds.
		var first = await CreateAsync("First leg");
		await Deps(api => api.CreateAsync(ObjectiveRef(first.Id), CheckpointRef(milestone), cancellationToken: Ct));
		Assert.False(await UnlockedAsync(milestone));
		await ShiftAsync(first.Id, ObjectiveStatus.Done);
		Assert.True(await UnlockedAsync(milestone));

		// What relied on it closed; something new blocking it takes its finish back, and steps the closed item back to Done.
		var report = await CreateAsync("Report");
		await Deps(api => api.CreateAsync(CheckpointRef(milestone), ObjectiveRef(report.Id), cancellationToken: Ct));
		await ShiftAsync(report.Id, ObjectiveStatus.Archived);
		var second = await CreateAsync("Second leg");
		await Deps(api => api.CreateAsync(ObjectiveRef(second.Id), CheckpointRef(milestone), cancellationToken: Ct));

		Assert.False(await UnlockedAsync(milestone));
		Assert.Equal(ObjectiveStatus.Done, await StatusAsync(report.Id));
	}

	[Fact]
	public async Task Joining_the_active_sprint_or_cycle_pushes_only_forward()
	{
		await Onrush(api => api.StartNewAsync(cancellationToken: Ct));
		var finished = await CreateAsync("Finished work");
		await ShiftAsync(finished.Id, ObjectiveStatus.Done);
		var archived = await CreateAsync("Archived work");
		await ShiftAsync(archived.Id, ObjectiveStatus.Archived);

		// A finished objective joins as it is.
		await Objective(api => api.AddToOnrushAsync(finished.Id, OnrushSlot.Active, Ct));
		Assert.Equal(ObjectiveStatus.Done, await StatusAsync(finished.Id));

		// An archived one cannot join as it stands: under Block the join is refused…
		await SetEnforcementAsync(BacklogStateEnforcementLevel.Block);
		var refused = await Assert.ThrowsAsync<StateEnforcementException>(() => Objective(api => api.AddToOnrushAsync(archived.Id, OnrushSlot.Active, Ct)));
		Assert.Equal(StateJudge.ClosedJoinRule, Assert.Single(refused.Decision.Conflicts).Rule);
		Assert.Equal(ObjectiveStatus.Archived, await StatusAsync(archived.Id));

		// …and under Auto it steps back to Done and joins, the active Polaris cycle alike.
		await SetEnforcementAsync(BacklogStateEnforcementLevel.Auto);
		await Objective(api => api.AddToOnrushAsync(archived.Id, OnrushSlot.Active, Ct));
		Assert.Equal(ObjectiveStatus.Done, await StatusAsync(archived.Id));
		await ShiftAsync(archived.Id, ObjectiveStatus.Archived);
		await Polaris(api => api.PlanExecutiveAsync(new PolarisExecutivePlan(PolarisExecutivePlanningMode.FromObjective, ObjectiveId: archived.Id), cancellationToken: Ct));
		Assert.Equal(ObjectiveStatus.Done, await StatusAsync(archived.Id));
	}
}
