using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Diagnostics;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Plaintorch.Api;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Enforcement;
using Pleiades.Plaintorch.Preferences;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Pleiades.Vault.Watcher;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The state enforcement engine (PEP103 Patch103.2): one state interceptor judges every save, on every pathway, and every
/// decision goes through the backlog state enforcement level — compensated under Auto, asked under Warn, refused under Block,
/// let through under Ignore, and refused at every level when it is systemic.
/// </summary>
public sealed class StateEngineTests : VaultTestBase
{
	private CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> Directive<T>(Func<IDirectiveApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDirectiveApi>()));
	private Task<T> Objective<T>(Func<IObjectiveApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IObjectiveApi>()));
	private Task<T> Deps<T>(Func<IDependencyApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDependencyApi>()));

	/// <summary>Runs as an API request would: someone can be asked, and with <paramref name="force"/> they confirmed.</summary>
	private Task<T> Interactive<T>(Func<IServiceProvider, Task<T>> action, bool force = false) => Vault.WithScopeAsync(services =>
	{
		var flags = services.GetRequiredService<PlaintorchRequestFlags>();
		flags.Interactive = true;
		flags.ForceCompensation = force;
		return action(services);
	});

	private Task SetEnforcementAsync(BacklogStateEnforcementLevel level)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<UserPreferenceService>().SetAsync(PreferenceKeys.BacklogStateEnforcement, level, Ct));

	private Task<DirectiveStatus> DirectiveStatusAsync(string id)
		=> Vault.QueryAsync(context => context.Directives.OfType<StellarDirective>().AsNoTracking().IgnoreAutoIncludes()
			.Where(directive => directive.Id == id).Select(directive => directive.Status).SingleAsync(Ct));

	private Task<ObjectiveStatus> ObjectiveStatusAsync(string id)
		=> Vault.QueryAsync(context => context.Objectives.AsNoTracking().IgnoreAutoIncludes()
			.Where(objective => objective.Id == id).Select(objective => objective.Status).SingleAsync(Ct));

	private Task<List<AuditLogEntry>> AuditAsync(string action)
		=> Vault.QueryAsync(context => context.AuditLogEntries.AsNoTracking().Where(entry => entry.Action == action).ToListAsync(Ct));

	private static EndpointRef DirectiveRef(string id) => new(DependencyEndpointKind.Directive, id);

	private static EndpointRef ObjectiveRef(string id) => new(DependencyEndpointKind.Objective, id);

	[Fact]
	public async Task Under_auto_a_finish_finishes_what_it_needs_down_the_tree()
	{
		await SetEnforcementAsync(BacklogStateEnforcementLevel.Auto);
		var campaign = await Directive(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Ct));
		var strike = await Directive(api => api.CreateFromParentAsync(campaign.Id, "Strike", cancellationToken: Ct));
		var sortie = await Objective(api => api.CreateFromDirectiveAsync(strike.Id, "Sortie", cancellationToken: Ct));
		var quest = await Objective(api => api.CreateFromDirectiveAsync(campaign.Id, "Quest", cancellationToken: Ct));

		// Finishing the campaign needs its children finished, and the subdirective needs its own: the moves cascade.
		await Directive(api => api.ShiftStellarWorkflowAsync(campaign.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Fulfilled), Ct));

		Assert.Equal(DirectiveStatus.Fulfilled, await DirectiveStatusAsync(campaign.Id));
		Assert.Equal(DirectiveStatus.Fulfilled, await DirectiveStatusAsync(strike.Id));
		Assert.Equal(ObjectiveStatus.Done, await ObjectiveStatusAsync(sortie.Id));
		Assert.Equal(ObjectiveStatus.Done, await ObjectiveStatusAsync(quest.Id));

		// Each move is on the record and its note follows its row.
		Assert.Equal(3, (await AuditAsync("state.compensate")).Count);
		Assert.Contains("status: Fulfilled", Vault.ReadVaultFile("Directives/Campaign/Strike/Strike.md"));
	}

	[Fact]
	public async Task Warn_asks_a_client_first_and_goes_ahead_once_forced()
	{
		var foundation = await Directive(api => api.CreateStandaloneAsync("Foundation", cancellationToken: Ct));
		var tower = await Objective(api => api.CreateStandaloneAsync("Tower", cancellationToken: Ct));
		await Deps(api => api.CreateAsync(DirectiveRef(foundation.Id), ObjectiveRef(tower.Id), cancellationToken: Ct));

		// Warn is the default: a client is asked, and told what blocks the change and what going ahead would move.
		var held = await Assert.ThrowsAsync<StateEnforcementException>(() =>
			Interactive(services => services.GetRequiredService<IObjectiveApi>().ShiftWorkflowAsync(tower.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Polaris), Ct)));
		Assert.Equal(StateDecisionOutcome.Confirm, held.Decision.Outcome);
		Assert.Equal(BacklogStateEnforcementLevel.Warn, held.Decision.Level);
		Assert.Contains("cannot start: it is blocked by 1 dependency", held.Message);
		Assert.Contains("finish 1 item", held.Message);
		var move = Assert.Single(held.Decision.Compensations);
		Assert.Equal(foundation.Id, move.Subject.Id);
		Assert.Equal(nameof(DirectiveStatus.Fulfilled), move.Status);
		Assert.Equal(ObjectiveStatus.Standby, await ObjectiveStatusAsync(tower.Id));

		// Forced, the change goes ahead with its compensation in the same save.
		var started = await Interactive(services => services.GetRequiredService<IObjectiveApi>().ShiftWorkflowAsync(tower.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Polaris), Ct), force: true);
		Assert.Equal(ObjectiveStatus.Polaris, started.Status);
		Assert.Equal(DirectiveStatus.Fulfilled, await DirectiveStatusAsync(foundation.Id));
		Assert.Contains("status: Fulfilled", Vault.ReadVaultFile("Directives/Foundation/Foundation.md"));
	}

	[Fact]
	public async Task Block_refuses_even_a_forced_change_and_ignore_lets_it_through()
	{
		var foundation = await Directive(api => api.CreateStandaloneAsync("Foundation", cancellationToken: Ct));
		var tower = await Objective(api => api.CreateStandaloneAsync("Tower", cancellationToken: Ct));
		await Deps(api => api.CreateAsync(DirectiveRef(foundation.Id), ObjectiveRef(tower.Id), cancellationToken: Ct));

		await SetEnforcementAsync(BacklogStateEnforcementLevel.Block);
		var blocked = await Assert.ThrowsAsync<StateEnforcementException>(() =>
			Interactive(services => services.GetRequiredService<IObjectiveApi>().ShiftWorkflowAsync(tower.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Polaris), Ct), force: true));
		Assert.Equal(StateDecisionOutcome.Refuse, blocked.Decision.Outcome);
		Assert.Contains("set to block", blocked.Message);

		// Ignore lets the change through as asked, moves nothing else, and puts what it broke on the record.
		await SetEnforcementAsync(BacklogStateEnforcementLevel.Ignore);
		var started = await Objective(api => api.ShiftWorkflowAsync(tower.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Polaris), Ct));
		Assert.Equal(ObjectiveStatus.Polaris, started.Status);
		Assert.Equal(DirectiveStatus.Planned, await DirectiveStatusAsync(foundation.Id));
		Assert.Contains(StateJudge.StartGateRule, Assert.Single(await AuditAsync("state.ignore")).DetailsJson);
	}

	[Fact]
	public async Task A_systemic_conflict_is_refused_at_every_level()
	{
		await SetEnforcementAsync(BacklogStateEnforcementLevel.Ignore);
		await Vault.WithScopeAsync(services => services.GetRequiredService<IOnrushSprintApi>().StartNewAsync(cancellationToken: Ct));

		var refused = await Assert.ThrowsAsync<StateEnforcementException>(() => Vault.WithScopeAsync(services =>
			services.GetRequiredService<IOnrushSprintApi>().StartNewAsync(cancellationToken: Ct)));
		Assert.Equal(StateDecisionOutcome.Refuse, refused.Decision.Outcome);
		var conflict = Assert.Single(refused.Decision.Conflicts);
		Assert.Equal(StateConflictKind.Systemic, conflict.Kind);
		Assert.Equal(StateJudge.SingleActiveOnrushRule, conflict.Rule);
	}

	[Fact]
	public async Task Planning_a_gated_objective_into_polaris_adds_it_without_starting_it()
	{
		var prerequisite = await Directive(api => api.CreateStandaloneAsync("Prerequisite", cancellationToken: Ct));
		var objective = await Objective(api => api.CreateStandaloneAsync("Gated work", cancellationToken: Ct));
		await Deps(api => api.CreateAsync(DirectiveRef(prerequisite.Id), ObjectiveRef(objective.Id), cancellationToken: Ct));

		// Joining the cycle starts an objective only where nothing blocks it (Patch103.3); its gate never stops the join
		// (it used to skip the gate altogether, I-001, then to refuse the plan).
		var planned = await Vault.WithScopeAsync(services =>
			services.GetRequiredService<IPolarisCycleApi>().PlanExecutiveAsync(
				new PolarisExecutivePlan(PolarisExecutivePlanningMode.FromObjective, ObjectiveId: objective.Id), cancellationToken: Ct));
		Assert.Equal(objective.Id, planned.Executive.IncentiveId);
		Assert.Equal(ObjectiveStatus.Standby, await ObjectiveStatusAsync(objective.Id));

		// Once the gate is met, a start is the user's to make.
		await Directive(api => api.ShiftStellarWorkflowAsync(prerequisite.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Fulfilled), Ct));
		Assert.Equal(ObjectiveStatus.Standby, await ObjectiveStatusAsync(objective.Id));
	}

	[Fact]
	public async Task A_note_edit_the_rules_refuse_keeps_the_note_and_stands_as_an_issue()
	{
		var foundation = await Directive(api => api.CreateStandaloneAsync("Foundation", cancellationToken: Ct));
		var tower = await Directive(api => api.CreateStandaloneAsync("Tower", cancellationToken: Ct));
		await Deps(api => api.CreateAsync(DirectiveRef(foundation.Id), DirectiveRef(tower.Id), cancellationToken: Ct));

		// The note asks to start the tower; nobody can confirm a note edit, so under Warn it is refused.
		const string note = "Directives/Tower/Tower.md";
		var edited = Vault.ReadVaultFile(note).Replace("status: Planned", "status: Active", StringComparison.Ordinal);
		Vault.WriteVaultFile(note, edited);
		await Vault.ReconcileWithIssuesAsync(Vault.AbsolutePath(note));

		Assert.Equal(DirectiveStatus.Planned, await DirectiveStatusAsync(tower.Id));
		Assert.Equal(edited, Vault.ReadVaultFile(note));
		var issue = Assert.Single(RefusalsOf(note));
		Assert.Equal(OperationSeverity.Error, issue.Severity);
		Assert.Contains("cannot start: it is blocked by 1 dependency", issue.Detail);
		Assert.DoesNotContain(foundation.Id, issue.Detail);

		// Once what blocks it is settled, the note's edit goes through and the issue clears.
		await Directive(api => api.ShiftStellarWorkflowAsync(foundation.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Fulfilled), Ct));
		await Vault.ReconcileWithIssuesAsync(Vault.AbsolutePath(note));
		Assert.Equal(DirectiveStatus.Active, await DirectiveStatusAsync(tower.Id));
		Assert.Empty(RefusalsOf(note));
	}

	[Fact]
	public async Task Clients_are_served_each_kinds_lifecycle_as_declared()
	{
		var lifecycles = await Vault.WithScopeAsync(services => Task.FromResult(services.GetRequiredService<ISystemApi>().GetLifecycles()));

		var objective = Assert.Single(lifecycles, kind => kind.Kind == nameof(Objective));
		Assert.Equal([LifecycleState.Deferred, LifecycleState.Open, LifecycleState.Started, LifecycleState.Finished], objective.States);
		Assert.Equal(Enum.GetNames<ObjectiveStatus>(), objective.Statuses.Select(status => status.Name));
		var polaris = Assert.Single(objective.Statuses, status => status.Name == nameof(ObjectiveStatus.Polaris));
		Assert.Equal((int)ObjectiveStatus.Polaris, polaris.Value);
		Assert.Equal(LifecycleState.Started, polaris.State);
		Assert.True(polaris.IsDefault);
		Assert.False(Assert.Single(objective.Statuses, status => status.Name == nameof(ObjectiveStatus.Archived)).IsDefault);

		// A checkpoint's state is no status: it is served its states alone, and it never starts.
		var checkpoint = Assert.Single(lifecycles, kind => kind.Kind == nameof(Checkpoint));
		Assert.Empty(checkpoint.Statuses);
		Assert.DoesNotContain(LifecycleState.Started, checkpoint.States);
	}

	private IReadOnlyList<OperationStatus> RefusalsOf(string note)
		=> Vault.GetSingleton<OperationStatusRegistry>()
			.GetActiveStatuses()
			.Where(status => status.ReasonCode == WatcherOperations.StateRefused
				&& string.Equals(status.ScopeKey, Vault.AbsolutePath(note), StringComparison.OrdinalIgnoreCase))
			.ToList();
}
