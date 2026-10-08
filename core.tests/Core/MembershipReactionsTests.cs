using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Enforcement;
using Pleiades.Plaintorch.Preferences;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The moves the engine makes when an objective joins (PEP103 Patch103.3): coming on board the active onrush sprint makes it
/// Onboard, joining the active Polaris cycle makes it Polaris, each from an open or finished status and only where nothing
/// blocks it. The planning sprint and a forecast move nothing until they begin, and leaving moves nothing.
/// </summary>
public sealed class MembershipReactionsTests : VaultTestBase
{
	private CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> Objective<T>(Func<IObjectiveApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IObjectiveApi>()));
	private Task<T> Directive<T>(Func<IDirectiveApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDirectiveApi>()));
	private Task<T> Onrush<T>(Func<IOnrushSprintApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IOnrushSprintApi>()));
	private Task<T> Polaris<T>(Func<IPolarisCycleApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IPolarisCycleApi>()));
	private Task<T> Deps<T>(Func<IDependencyApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDependencyApi>()));

	private Task SetEnforcementAsync(BacklogStateEnforcementLevel level)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<UserPreferenceService>().SetAsync(PreferenceKeys.BacklogStateEnforcement, level, Ct));

	private Task<ObjectiveStatus> StatusAsync(string id)
		=> Vault.QueryAsync(context => context.Objectives.AsNoTracking().IgnoreAutoIncludes()
			.Where(objective => objective.Id == id).Select(objective => objective.Status).SingleAsync(Ct));

	private Task<DirectiveStatus> DirectiveStatusAsync(string id)
		=> Vault.QueryAsync(context => context.Directives.OfType<StellarDirective>().AsNoTracking().IgnoreAutoIncludes()
			.Where(directive => directive.Id == id).Select(directive => directive.Status).SingleAsync(Ct));

	/// <summary>Creates a standalone objective and shifts it to <paramref name="status"/> unless it is the default.</summary>
	private async Task<Objective> ObjectiveInAsync(string title, ObjectiveStatus status = ObjectiveStatus.Standby)
	{
		var objective = await Objective(api => api.CreateStandaloneAsync(title, cancellationToken: Ct));
		return status == ObjectiveStatus.Standby
			? objective
			: await Objective(api => api.ShiftWorkflowAsync(objective.Id, new ObjectiveWorkflowShift(status), Ct));
	}

	private Task<PolarisExecutivePlanResult> PlanAsync(string objectiveId, string? cycleId = null)
		=> Polaris(api => api.PlanExecutiveAsync(new PolarisExecutivePlan(PolarisExecutivePlanningMode.FromObjective, ObjectiveId: objectiveId), cycleId, Ct));

	private static EndpointRef DirectiveRef(string id) => new(DependencyEndpointKind.Directive, id);

	private static EndpointRef ObjectiveRef(string id) => new(DependencyEndpointKind.Objective, id);

	[Fact]
	public async Task Coming_on_board_the_active_sprint_makes_open_and_finished_objectives_onboard()
	{
		var sprint = await Onrush(api => api.StartNewAsync(cancellationToken: Ct));
		var standby = await ObjectiveInAsync("Standby work");
		var blocked = await ObjectiveInAsync("Blocked work", ObjectiveStatus.Blocked);
		var done = await ObjectiveInAsync("Done work", ObjectiveStatus.Done);
		var started = await ObjectiveInAsync("Started work", ObjectiveStatus.Polaris);
		var failed = await ObjectiveInAsync("Failed work", ObjectiveStatus.Failed);
		await Vault.BeginObjectiveBoundaryAsync(standby.Id);

		foreach (var objective in new[] { standby, blocked, done, started, failed })
		{
			await Objective(api => api.AddToOnrushAsync(objective.Id, sprint.Id, Ct));
		}

		Assert.Equal(ObjectiveStatus.Onboard, await StatusAsync(standby.Id));
		Assert.Equal(ObjectiveStatus.Onboard, await StatusAsync(blocked.Id));
		// A finished objective is taken up again; one already started, or set aside, keeps its status.
		Assert.Equal(ObjectiveStatus.Onboard, await StatusAsync(done.Id));
		Assert.Equal(ObjectiveStatus.Polaris, await StatusAsync(started.Id));
		Assert.Equal(ObjectiveStatus.Failed, await StatusAsync(failed.Id));

		// The note follows, and each move is on the record.
		Assert.Contains("status: Onboard", Vault.ReadVaultFile("Objectives/Standby work.md"));
		var joins = await Vault.QueryAsync(context => context.AuditLogEntries.AsNoTracking().Where(entry => entry.Action == "state.join").ToListAsync(Ct));
		Assert.Equal(3, joins.Count);
	}

	[Fact]
	public async Task The_planning_sprint_moves_nothing_until_it_begins()
	{
		await Onrush(api => api.PlanAsync(new OnrushSprintPlan("Next sprint"), Ct));
		var standby = await ObjectiveInAsync("Planned work");
		var done = await ObjectiveInAsync("Done ahead", ObjectiveStatus.Done);
		var started = await ObjectiveInAsync("Running work", ObjectiveStatus.Polaris);
		foreach (var objective in new[] { standby, done, started })
		{
			await Objective(api => api.AddToOnrushAsync(objective.Id, OnrushSprint.PlanningPlaceholderId, Ct));
		}

		Assert.Equal(ObjectiveStatus.Standby, await StatusAsync(standby.Id));
		Assert.Equal(ObjectiveStatus.Done, await StatusAsync(done.Id));

		// Beginning the sprint brings its members on board, as joining it once active would.
		var begun = await Onrush(api => api.BeginAsync(OnrushSprint.PlanningPlaceholderId, cancellationToken: Ct));
		Assert.Equal(ObjectiveStatus.Onboard, await StatusAsync(standby.Id));
		Assert.Equal(ObjectiveStatus.Onboard, await StatusAsync(done.Id));
		Assert.Equal(ObjectiveStatus.Polaris, await StatusAsync(started.Id));
		var member = await Vault.QueryAsync(context => context.Objectives.AsNoTracking().IgnoreAutoIncludes().SingleAsync(objective => objective.Id == standby.Id, Ct));
		Assert.Equal(begun.Id, member.OnrushSprintId);
	}

	[Fact]
	public async Task An_ended_sprint_taken_up_again_does_not_reopen_its_finished_members()
	{
		var sprint = await Onrush(api => api.StartNewAsync(cancellationToken: Ct));
		var objective = await ObjectiveInAsync("Sprint work");
		await Objective(api => api.AddToOnrushAsync(objective.Id, sprint.Id, Ct));
		await Objective(api => api.ShiftWorkflowAsync(objective.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Done), Ct));
		await Onrush(api => api.EndAsync(sprint.Id, cancellationToken: Ct));

		await Onrush(api => api.UpdateAsync(sprint.Id, new OnrushSprintUpdate(EndDate: new Optional<DateOnly?>(null)), Ct));

		Assert.Equal(ObjectiveStatus.Done, await StatusAsync(objective.Id));
	}

	[Fact]
	public async Task Leaving_a_sprint_or_a_cycle_moves_nothing()
	{
		var sprint = await Onrush(api => api.StartNewAsync(cancellationToken: Ct));
		var sprintWork = await ObjectiveInAsync("Sprint work");
		await Objective(api => api.AddToOnrushAsync(sprintWork.Id, sprint.Id, Ct));
		await Objective(api => api.RemoveFromOnrushAsync(sprintWork.Id, Ct));
		Assert.Equal(ObjectiveStatus.Onboard, await StatusAsync(sprintWork.Id));

		var cycleWork = await ObjectiveInAsync("Cycle work");
		var planned = await PlanAsync(cycleWork.Id);
		Assert.Equal(ObjectiveStatus.Polaris, await StatusAsync(cycleWork.Id));
		await Vault.WithScopeAsync(services => services.GetRequiredService<IPolarisCycleApi>().RemoveExecutiveAsync(planned.Executive.Id, Ct));
		Assert.Equal(ObjectiveStatus.Polaris, await StatusAsync(cycleWork.Id));
	}

	[Fact]
	public async Task Joining_the_active_cycle_starts_an_objective_only_where_nothing_blocks_it()
	{
		// Even under Block, a blocked objective joins: the engine simply leaves its status, and nothing is compensated.
		await SetEnforcementAsync(BacklogStateEnforcementLevel.Block);
		var standby = await ObjectiveInAsync("Clear work");
		var done = await ObjectiveInAsync("Finished work", ObjectiveStatus.Done);
		var failed = await ObjectiveInAsync("Failed work", ObjectiveStatus.Failed);
		var gated = await ObjectiveInAsync("Gated work");
		var prerequisite = await Directive(api => api.CreateStandaloneAsync("Prerequisite", cancellationToken: Ct));
		await Deps(api => api.CreateAsync(DirectiveRef(prerequisite.Id), ObjectiveRef(gated.Id), cancellationToken: Ct));

		foreach (var objective in new[] { standby, done, failed, gated })
		{
			await PlanAsync(objective.Id);
		}

		Assert.Equal(ObjectiveStatus.Polaris, await StatusAsync(standby.Id));
		Assert.Equal(ObjectiveStatus.Polaris, await StatusAsync(done.Id));
		Assert.Equal(ObjectiveStatus.Failed, await StatusAsync(failed.Id));
		Assert.Equal(ObjectiveStatus.Standby, await StatusAsync(gated.Id));
		Assert.Equal(DirectiveStatus.Planned, await DirectiveStatusAsync(prerequisite.Id));
		var executives = await Vault.QueryAsync(context => context.Set<Executive>().AsNoTracking().IgnoreAutoIncludes().CountAsync(Ct));
		Assert.Equal(4, executives);
	}

	[Fact]
	public async Task Under_auto_a_blocked_objective_still_moves_nothing_else()
	{
		await SetEnforcementAsync(BacklogStateEnforcementLevel.Auto);
		var prerequisite = await ObjectiveInAsync("Prerequisite");
		var gated = await ObjectiveInAsync("Gated work");
		await Deps(api => api.CreateAsync(ObjectiveRef(prerequisite.Id), ObjectiveRef(gated.Id), cancellationToken: Ct));

		await PlanAsync(gated.Id);

		Assert.Equal(ObjectiveStatus.Standby, await StatusAsync(gated.Id));
		Assert.Equal(ObjectiveStatus.Standby, await StatusAsync(prerequisite.Id));
	}

	[Fact]
	public async Task One_join_can_unblock_another_in_the_same_save()
	{
		var lead = await ObjectiveInAsync("Lead");
		var follow = await ObjectiveInAsync("Follow");
		// The follower may start once the lead has started.
		await Deps(api => api.CreateAsync(ObjectiveRef(lead.Id), ObjectiveRef(follow.Id), DependencyTrigger.OnBegin, cancellationToken: Ct));
		var forecast = await Polaris(api => api.PlanAsync(DateOnly.FromDateTime(DateTime.Today), 0, cancellationToken: Ct));
		await PlanAsync(follow.Id, forecast.Id);
		await PlanAsync(lead.Id, forecast.Id);
		Assert.Equal(ObjectiveStatus.Standby, await StatusAsync(follow.Id));

		// Beginning the day starts both, whichever the engine tries first.
		await Polaris(api => api.BeginAsync(forecast.Id, cancellationToken: Ct));

		Assert.Equal(ObjectiveStatus.Polaris, await StatusAsync(lead.Id));
		Assert.Equal(ObjectiveStatus.Polaris, await StatusAsync(follow.Id));
	}

	[Fact]
	public async Task A_note_that_brings_its_objective_on_board_is_followed_by_the_engine()
	{
		var sprint = await Onrush(api => api.StartNewAsync(cancellationToken: Ct));
		var objective = await ObjectiveInAsync("Quest");
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);
		const string note = "Objectives/Quest.md";

		Vault.WriteVaultFile(note, Vault.ReadVaultFile(note).Replace("status: Standby", $"status: Standby{Environment.NewLine}onrush: {sprint.Id}", StringComparison.Ordinal));
		await Vault.ReconcileAsync(Vault.AbsolutePath(note));

		Assert.Equal(ObjectiveStatus.Onboard, await StatusAsync(objective.Id));
		Assert.Contains("status: Onboard", Vault.ReadVaultFile(note));
	}

	[Fact]
	public async Task A_status_the_save_sets_itself_is_kept()
	{
		var sprint = await Onrush(api => api.StartNewAsync(cancellationToken: Ct));
		var objective = await ObjectiveInAsync("Quest", ObjectiveStatus.Done);
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);
		const string note = "Objectives/Quest.md";

		// The note joins the sprint and asks for Blocked in the same edit: the edit wins.
		Vault.WriteVaultFile(note, Vault.ReadVaultFile(note).Replace("status: Done", $"status: Blocked{Environment.NewLine}onrush: {sprint.Id}", StringComparison.Ordinal));
		await Vault.ReconcileAsync(Vault.AbsolutePath(note));

		Assert.Equal(ObjectiveStatus.Blocked, await StatusAsync(objective.Id));
	}

	[Fact]
	public async Task A_finished_objective_is_not_brought_back_under_a_directive_that_is_over()
	{
		await SetEnforcementAsync(BacklogStateEnforcementLevel.Block);
		var campaign = await Directive(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Ct));
		var quest = await Objective(api => api.CreateFromDirectiveAsync(campaign.Id, "Quest", cancellationToken: Ct));
		await Objective(api => api.ShiftWorkflowAsync(quest.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Done), Ct));
		await Directive(api => api.ShiftStellarWorkflowAsync(campaign.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Over), Ct));
		var sprint = await Onrush(api => api.StartNewAsync(cancellationToken: Ct));

		// The join goes ahead; taking the objective up again would bring open work under the Over directive, so it stays done.
		await Objective(api => api.AddToOnrushAsync(quest.Id, sprint.Id, Ct));

		Assert.Equal(ObjectiveStatus.Done, await StatusAsync(quest.Id));
		Assert.Equal(DirectiveStatus.Over, await DirectiveStatusAsync(campaign.Id));
		var member = await Vault.QueryAsync(context => context.Objectives.AsNoTracking().IgnoreAutoIncludes().SingleAsync(objective => objective.Id == quest.Id, Ct));
		Assert.Equal(sprint.Id, member.OnrushSprintId);
	}
}
