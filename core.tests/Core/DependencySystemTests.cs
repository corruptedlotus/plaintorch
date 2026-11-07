using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Enforcement;
using Pleiades.Plaintorch.Preferences;
using Pleiades.Plaintorch.State;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The PEP101 dependency system: source-triggered satisfaction, target-constrained gating, the emitted lock
/// (separate from status), fate materialization freeze, CalDAV-style eventive references, checkpoints with
/// tolls/conditions, unlock cascades, and the structural validator.
/// </summary>
public sealed class DependencySystemTests : VaultTestBase
{
	private CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> Directive<T>(Func<IDirectiveApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDirectiveApi>()));
	private Task<T> Objective<T>(Func<IObjectiveApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IObjectiveApi>()));
	private Task<T> Declarative<T>(Func<IDeclarativeApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDeclarativeApi>()));
	private Task<T> Deps<T>(Func<IDependencyApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDependencyApi>()));
	private Task Deps(Func<IDependencyApi, Task> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDependencyApi>()));
	private Task<T> Onrush<T>(Func<IOnrushSprintApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IOnrushSprintApi>()));
	private Task Onrush(Func<IOnrushSprintApi, Task> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IOnrushSprintApi>()));

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

	private static EndpointRef DirectiveRef(string id) => new(DependencyEndpointKind.Directive, id);
	private static EndpointRef CheckpointRef(string id) => new(DependencyEndpointKind.Checkpoint, id);

	[Fact]
	public async Task Endpoint_search_returns_the_endpoint_kinds_and_excludes_the_rest(/* PEP102 */)
	{
		var directive = await Directive(api => api.CreateStandaloneAsync("Searchable Directive", cancellationToken: Ct));
		var objective = await Objective(api => api.CreateStandaloneAsync("Searchable Objective", cancellationToken: Ct));
		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Searchable Fate", Date: new DateOnly(2026, 1, 1)), Ct));
		// None of these may appear: a lunar directive and a decree are not endpoint kinds at all.
		var lunar = await Directive(api => api.CreateLunarAsync("Searchable Moon Law", cancellationToken: Ct));
		var decree = await Declarative(api => api.CreateDecreeAsync(new DecreePlan("Searchable Decree"), Ct));
		var checkpoint = await Deps(api => api.CreateCheckpointAsync("Searchable Gate", cancellationToken: Ct));

		var hits = await Deps(api => api.SearchEndpointsAsync(new SearchRequest("Searchable"), Ct));

		Assert.Contains(hits, hit => hit.Kind == DependencyEndpointKind.Directive && hit.Id == directive.Id);
		Assert.Contains(hits, hit => hit.Kind == DependencyEndpointKind.Objective && hit.Id == objective.Id);
		Assert.Contains(hits, hit => hit.Kind == DependencyEndpointKind.Fate && hit.Id == fate.Id);
		Assert.Contains(hits, hit => hit.Kind == DependencyEndpointKind.Checkpoint && hit.Id == checkpoint.Id);
		Assert.DoesNotContain(hits, hit => hit.Id == lunar.Id);
		Assert.DoesNotContain(hits, hit => hit.Id == decree.Id);
	}

	[Fact]
	public async Task Finish_triggered_begin_constraint_gates_the_target()
	{
		var source = await Directive(api => api.CreateStandaloneAsync("Foundation", cancellationToken: Ct));
		var target = await Directive(api => api.CreateStandaloneAsync("Tower", cancellationToken: Ct));

		await Deps(api => api.CreateAsync(DirectiveRef(source.Id), DirectiveRef(target.Id), cancellationToken: Ct));

		// Target cannot begin while the source is unfinished; the lock is emitted but the status is untouched. Nobody can be
		// asked here, so under Warn the shift is refused, and it says what blocks it rather than which item does (PEP103).
		var refused = await Assert.ThrowsAsync<StateEnforcementException>(() => Directive(api =>
			api.ShiftStellarWorkflowAsync(target.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Active), Ct)));
		Assert.Equal(StateDecisionOutcome.Refuse, refused.Decision.Outcome);
		Assert.Contains("cannot start: it is blocked by 1 dependency", refused.Message);
		Assert.DoesNotContain(source.Id, refused.Message);
		var lockedView = await Deps(api => api.GetLockAsync(target.Id, Ct));
		Assert.True(lockedView.BlockedBegin);
		var stillPlanned = await Directive(api => api.GetAsync(target.Id, Ct));
		Assert.Equal(DirectiveStatus.Planned, Assert.IsType<StellarDirective>(stillPlanned).Status);

		// Finishing the source satisfies the dependency and opens the gate.
		await Directive(api => api.ShiftStellarWorkflowAsync(source.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Fulfilled), Ct));
		var openView = await Deps(api => api.GetLockAsync(target.Id, Ct));
		Assert.False(openView.BlockedBegin);
		var begun = await Directive(api => api.ShiftStellarWorkflowAsync(target.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Active), Ct));
		Assert.Equal(DirectiveStatus.Active, begun.Status);
	}

	[Fact]
	public async Task Objective_begin_is_gated_until_the_source_finishes()
	{
		var source = await Directive(api => api.CreateStandaloneAsync("Prereq", cancellationToken: Ct));
		var objective = await Objective(api => api.CreateStandaloneAsync("Blocked work", cancellationToken: Ct));

		await Deps(api => api.CreateAsync(DirectiveRef(source.Id), new EndpointRef(DependencyEndpointKind.Objective, objective.Id), cancellationToken: Ct));

		// An objective begins when it is set to Polaris (PEP101), so that is the shift the begin gate refuses.
		await Assert.ThrowsAsync<StateEnforcementException>(() => Objective(api =>
			api.ShiftWorkflowAsync(objective.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Polaris), Ct)));

		await Directive(api => api.ShiftStellarWorkflowAsync(source.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Over), Ct));
		var promoted = await Objective(api => api.ShiftWorkflowAsync(objective.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Polaris), Ct));
		Assert.Equal(ObjectiveStatus.Polaris, promoted.Status);
	}

	[Fact]
	public async Task An_objective_joining_an_onrush_has_not_begun(/* PEP101 */)
	{
		var source = await Directive(api => api.CreateStandaloneAsync("Prereq", cancellationToken: Ct));
		var objective = await Objective(api => api.CreateStandaloneAsync("Sprint work", cancellationToken: Ct));
		var gate = await Deps(api => api.CreateAsync(DirectiveRef(source.Id), ObjectiveRef(objective.Id), cancellationToken: Ct));

		// Onboard is not a begin phase, so its begin gate does not stand in the way, and the gate still concerns the next
		// transition the objective has to make.
		var onrush = await Objective(api => api.ShiftWorkflowAsync(objective.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Onboard), Ct));
		Assert.Equal(ObjectiveStatus.Onboard, onrush.Status);
		Assert.True((await Deps(api => api.ListAsync(objective.Id, Ct))).Single(dependency => dependency.Id == gate.Id).GatesNextTransition);
		Assert.True((await Deps(api => api.GetLockAsync(objective.Id, Ct))).BlockedBegin);

		// On the source side, a begin-triggered edge from an objective waits for Polaris.
		var downstream = await Directive(api => api.CreateStandaloneAsync("Downstream", cancellationToken: Ct));
		var fromObjective = await Deps(api => api.CreateAsync(ObjectiveRef(objective.Id), DirectiveRef(downstream.Id), DependencyTrigger.OnBegin, cancellationToken: Ct));
		Assert.False(fromObjective.Satisfied);

		await Directive(api => api.ShiftStellarWorkflowAsync(source.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Fulfilled), Ct));
		await Objective(api => api.ShiftWorkflowAsync(objective.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Polaris), Ct));
		Assert.True((await Deps(api => api.ListAsync(downstream.Id, Ct))).Single(dependency => dependency.Id == fromObjective.Id).Satisfied);
	}

	[Fact]
	public async Task Checkpoint_unlocks_only_when_dependencies_toll_and_condition_are_all_met()
	{
		// Bank Celestron by settling an objective.
		var reward = await Objective(api => api.CreateStandaloneAsync("Payday", cancellationToken: Ct));
		await Objective(api => api.UpdateAsync(reward.Id, new ObjectiveUpdate(CelestronValue: 10), Ct));
		await Objective(api => api.ShiftWorkflowAsync(reward.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Done), Ct));
		var bankedBefore = await BankedAsync();
		Assert.True(bankedBefore >= 5);

		var source = await Directive(api => api.CreateStandaloneAsync("Gate prereq", cancellationToken: Ct));
		await Directive(api => api.ShiftStellarWorkflowAsync(source.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Fulfilled), Ct));
		var checkpoint = await Deps(api => api.CreateCheckpointAsync("Toll Gate", celestronToll: 5, externalCondition: false, cancellationToken: Ct));
		await Deps(api => api.CreateAsync(DirectiveRef(source.Id), CheckpointRef(checkpoint.Id), cancellationToken: Ct));

		// Dependency is satisfied, but toll unpaid and condition unmet keep it locked.
		Assert.False((await Deps(api => api.GetCheckpointAsync(checkpoint.Id, Ct)))!.Unlocked);

		await Deps(api => api.PayTollAsync(checkpoint.Id, Ct));
		var afterToll = await Deps(api => api.GetCheckpointAsync(checkpoint.Id, Ct));
		Assert.True(afterToll!.TollPaid);
		Assert.False(afterToll.Unlocked); // external condition still unmet
		Assert.Equal(bankedBefore - 5, await BankedAsync()); // toll debited the ledger by exactly the toll

		await Deps(api => api.SetExternalConditionAsync(checkpoint.Id, true, Ct));
		Assert.True((await Deps(api => api.GetCheckpointAsync(checkpoint.Id, Ct)))!.Unlocked);
	}

	[Fact]
	public async Task A_checkpoint_is_reconciled_even_when_the_vault_has_no_edge(/* PEP101, PEP103 patch 5 */)
	{
		// With no dependency anywhere, each checkpoint's unlock rests on its own toll and condition. The reconciler used to
		// skip checkpoints entirely until some unrelated edge existed. One with nothing to wait for at all is not finished
		// (user ruling, I-036): only a checkpoint that has something to wait for unlocks.
		var free = await Deps(api => api.CreateCheckpointAsync("Free", cancellationToken: Ct));
		var permit = await Deps(api => api.CreateCheckpointAsync("Permit", externalCondition: false, cancellationToken: Ct));
		Assert.False(free.Unlocked);
		Assert.False(permit.Unlocked);
		Assert.Empty(await Deps(api => api.ListAsync(cancellationToken: Ct)));

		await Deps(api => api.SetExternalConditionAsync(permit.Id, true, Ct));
		Assert.True((await Deps(api => api.GetCheckpointAsync(permit.Id, Ct)))!.Unlocked);
	}

	[Fact]
	public async Task A_checkpoint_toll_is_suppressed_until_its_due()
	{
		var source = await Directive(api => api.CreateStandaloneAsync("Gate prereq", cancellationToken: Ct));
		await Directive(api => api.ShiftStellarWorkflowAsync(source.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Fulfilled), Ct));
		var checkpoint = await Deps(api => api.CreateCheckpointAsync("Deadline Gate", celestronToll: 5, cancellationToken: Ct));
		await Deps(api => api.CreateAsync(DirectiveRef(source.Id), CheckpointRef(checkpoint.Id), cancellationToken: Ct));

		// Dependency met, but the toll is unpaid and there is no due, so it is owed now: the checkpoint stays locked.
		Assert.False((await Deps(api => api.GetCheckpointAsync(checkpoint.Id, Ct)))!.Unlocked);

		// A future due suppresses the toll — there is still time — so the checkpoint unlocks without paying.
		var future = Due.On(DateOnly.FromDateTime(DateTime.Today).AddDays(3));
		await Deps(api => api.UpdateCheckpointAsync(checkpoint.Id, new CheckpointUpdate(Due: future), Ct));
		Assert.True((await Deps(api => api.GetCheckpointAsync(checkpoint.Id, Ct)))!.Unlocked);

		// What is done is done (PEP103 patch 5): once it has finished, its due no longer brings the toll due.
		var past = Due.At(DateTime.Now.AddDays(-1));
		await Deps(api => api.UpdateCheckpointAsync(checkpoint.Id, new CheckpointUpdate(Due: past), Ct));
		Assert.True((await Deps(api => api.GetCheckpointAsync(checkpoint.Id, Ct)))!.Unlocked);

		// A checkpoint still locked when its due passes owes its toll, and paying it unlocks the checkpoint late.
		var late = await Deps(api => api.CreateCheckpointAsync("Late Gate", celestronToll: 0, cancellationToken: Ct));
		var blocker = await Directive(api => api.CreateStandaloneAsync("Late prereq", cancellationToken: Ct));
		await Deps(api => api.CreateAsync(DirectiveRef(blocker.Id), CheckpointRef(late.Id), cancellationToken: Ct));
		await Deps(api => api.UpdateCheckpointAsync(late.Id, new CheckpointUpdate(Due: past), Ct));
		await Directive(api => api.ShiftStellarWorkflowAsync(blocker.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Fulfilled), Ct));
		Assert.False((await Deps(api => api.GetCheckpointAsync(late.Id, Ct)))!.Unlocked);
		await Deps(api => api.PayTollAsync(late.Id, Ct));
		Assert.True((await Deps(api => api.GetCheckpointAsync(late.Id, Ct)))!.Unlocked);
	}

	[Fact]
	public async Task Paying_a_toll_without_enough_celestron_is_rejected()
	{
		var checkpoint = await Deps(api => api.CreateCheckpointAsync("Expensive", celestronToll: 100, cancellationToken: Ct));
		await Assert.ThrowsAsync<InvalidOperationException>(() => Deps(api => api.PayTollAsync(checkpoint.Id, Ct)));
	}

	[Fact]
	public async Task Checkpoint_unlocks_cascade_through_dependent_checkpoints()
	{
		var source = await Directive(api => api.CreateStandaloneAsync("Root", cancellationToken: Ct));
		await Directive(api => api.ShiftStellarWorkflowAsync(source.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Fulfilled), Ct));

		var first = await Deps(api => api.CreateCheckpointAsync("First", cancellationToken: Ct));
		var second = await Deps(api => api.CreateCheckpointAsync("Second", cancellationToken: Ct));
		await Deps(api => api.CreateAsync(DirectiveRef(source.Id), CheckpointRef(first.Id), cancellationToken: Ct));
		await Deps(api => api.CreateAsync(CheckpointRef(first.Id), CheckpointRef(second.Id), cancellationToken: Ct));

		Assert.True((await Deps(api => api.GetCheckpointAsync(first.Id, Ct)))!.Unlocked);
		Assert.True((await Deps(api => api.GetCheckpointAsync(second.Id, Ct)))!.Unlocked);
	}

	[Fact]
	public async Task Validator_rejects_self_cycle_checkpoint_trigger_and_lunar_endpoints()
	{
		var a = await Directive(api => api.CreateStandaloneAsync("A", cancellationToken: Ct));
		var b = await Directive(api => api.CreateStandaloneAsync("B", cancellationToken: Ct));
		var checkpoint = await Deps(api => api.CreateCheckpointAsync("CP", cancellationToken: Ct));
		var lunar = await Directive(api => api.CreateLunarAsync("Moon Law", cancellationToken: Ct));

		// Self-dependency.
		await Assert.ThrowsAsync<InvalidOperationException>(() => Deps(api => api.CreateAsync(DirectiveRef(a.Id), DirectiveRef(a.Id), cancellationToken: Ct)));

		// Cycle: A blocks B, then B blocks A.
		await Deps(api => api.CreateAsync(DirectiveRef(a.Id), DirectiveRef(b.Id), cancellationToken: Ct));
		await Assert.ThrowsAsync<InvalidOperationException>(() => Deps(api => api.CreateAsync(DirectiveRef(b.Id), DirectiveRef(a.Id), cancellationToken: Ct)));

		// A checkpoint is one-shot: its only event is its unlock, so a checkpoint source may not carry a trigger.
		await Assert.ThrowsAsync<InvalidOperationException>(() => Deps(api => api.CreateAsync(CheckpointRef(checkpoint.Id), DirectiveRef(b.Id), trigger: DependencyTrigger.OnFinish, cancellationToken: Ct)));

		// Lunar directives are excluded.
		await Assert.ThrowsAsync<InvalidOperationException>(() => Deps(api => api.CreateAsync(DirectiveRef(lunar.Id), DirectiveRef(b.Id), cancellationToken: Ct)));
	}

	[Fact]
	public async Task Validator_rejects_a_duplicate_edge_including_the_defaulted_trigger(/* PEP102 */)
	{
		var a = await Directive(api => api.CreateStandaloneAsync("Prereq", cancellationToken: Ct));
		var b = await Directive(api => api.CreateStandaloneAsync("Dependant", cancellationToken: Ct));

		// First edge takes the defaults (finish-triggered, begin-constraining).
		await Deps(api => api.CreateAsync(DirectiveRef(a.Id), DirectiveRef(b.Id), cancellationToken: Ct));

		// An identical edge is refused — even when it names the finish-trigger the first left to default, which
		// the application check resolves to the same relation though a raw-column unique index would not.
		await Assert.ThrowsAsync<InvalidOperationException>(() => Deps(api => api.CreateAsync(DirectiveRef(a.Id), DirectiveRef(b.Id), cancellationToken: Ct)));
		await Assert.ThrowsAsync<InvalidOperationException>(() => Deps(api => api.CreateAsync(DirectiveRef(a.Id), DirectiveRef(b.Id), trigger: DependencyTrigger.OnFinish, constraint: DependencyConstraint.ToBegin, cancellationToken: Ct)));

		// A different constraint is a different relation and is allowed.
		var distinct = await Deps(api => api.CreateAsync(DirectiveRef(a.Id), DirectiveRef(b.Id), constraint: DependencyConstraint.ToFinish, cancellationToken: Ct));
		Assert.True(distinct.Id > 0);
	}

	[Fact]
	public async Task Logical_possibility_allows_a_temporally_orderable_begin_finish_cycle(/* PEP102 */)
	{
		var a = await Directive(api => api.CreateStandaloneAsync("A", cancellationToken: Ct));
		var b = await Directive(api => api.CreateStandaloneAsync("B", cancellationToken: Ct));

		// A's begin gates B's begin (A.begin < B.begin).
		await Deps(api => api.CreateAsync(DirectiveRef(a.Id), DirectiveRef(b.Id), DependencyTrigger.OnBegin, DependencyConstraint.ToBegin, Ct));

		// B's begin gates A's finish (B.begin < A.finish). This closes a node-level loop A→B→A, which the old
		// acyclicity check forbade, but it orders cleanly as A.begin < B.begin < A.finish — so it is allowed.
		var possible = await Deps(api => api.CreateAsync(DirectiveRef(b.Id), DirectiveRef(a.Id), DependencyTrigger.OnBegin, DependencyConstraint.ToFinish, Ct));
		Assert.True(possible.Id > 0);
	}

	[Fact]
	public async Task Logical_possibility_rejects_an_unorderable_finish_before_begin_loop(/* PEP102 */)
	{
		var a = await Directive(api => api.CreateStandaloneAsync("A", cancellationToken: Ct));
		var b = await Directive(api => api.CreateStandaloneAsync("B", cancellationToken: Ct));

		// A.finish < B.begin, then asking B.finish < A.begin closes an impossible loop:
		// A.begin < A.finish < B.begin < B.finish < A.begin.
		await Deps(api => api.CreateAsync(DirectiveRef(a.Id), DirectiveRef(b.Id), DependencyTrigger.OnFinish, DependencyConstraint.ToBegin, Ct));
		await Assert.ThrowsAsync<InvalidOperationException>(() => Deps(api =>
			api.CreateAsync(DirectiveRef(b.Id), DirectiveRef(a.Id), DependencyTrigger.OnFinish, DependencyConstraint.ToBegin, Ct)));
	}

	[Fact]
	public async Task Logical_possibility_orders_through_a_checkpoint_as_one_point(/* PEP102, I-034 */)
	{
		var a = await Directive(api => api.CreateStandaloneAsync("A", cancellationToken: Ct));
		var b = await Directive(api => api.CreateStandaloneAsync("B", cancellationToken: Ct));
		var checkpoint = await Deps(api => api.CreateCheckpointAsync("Gate", cancellationToken: Ct));

		// A checkpoint is a single point, its unlock: a chain A.finish < gate < B.begin is accepted like any chain...
		var into = await Deps(api => api.CreateAsync(DirectiveRef(a.Id), CheckpointRef(checkpoint.Id), cancellationToken: Ct));
		var outOf = await Deps(api => api.CreateAsync(CheckpointRef(checkpoint.Id), DirectiveRef(b.Id), cancellationToken: Ct));
		Assert.True(into.Id > 0 && outOf.Id > 0);

		// ...and closing it back onto A is refused, the path naming the checkpoint's unlock.
		var looping = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			Deps(api => api.CreateAsync(DirectiveRef(b.Id), DirectiveRef(a.Id), cancellationToken: Ct)));
		Assert.Contains("logically impossible", looping.Message);
		Assert.Contains($"Checkpoint:{checkpoint.Id}.unlock", looping.Message);

		// X.finish < checkpoint < X.begin would deadlock X, so it is refused too.
		var x = await Directive(api => api.CreateStandaloneAsync("X", cancellationToken: Ct));
		var loop = await Deps(api => api.CreateCheckpointAsync("Loop", cancellationToken: Ct));
		await Deps(api => api.CreateAsync(DirectiveRef(x.Id), CheckpointRef(loop.Id), cancellationToken: Ct));
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			Deps(api => api.CreateAsync(CheckpointRef(loop.Id), DirectiveRef(x.Id), cancellationToken: Ct)));

		// Through two checkpoints: X.finish < loop < relay, so relay < X.begin is refused.
		var relay = await Deps(api => api.CreateCheckpointAsync("Relay", cancellationToken: Ct));
		await Deps(api => api.CreateAsync(CheckpointRef(loop.Id), CheckpointRef(relay.Id), cancellationToken: Ct));
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			Deps(api => api.CreateAsync(CheckpointRef(relay.Id), DirectiveRef(x.Id), cancellationToken: Ct)));

		// Not a loop: the gate's prerequisites gain another (Y.finish < gate < B.begin).
		var y = await Directive(api => api.CreateStandaloneAsync("Y", cancellationToken: Ct));
		var another = await Deps(api => api.CreateAsync(DirectiveRef(y.Id), CheckpointRef(checkpoint.Id), cancellationToken: Ct));
		Assert.True(another.Id > 0);
	}

	[Fact]
	public async Task A_stored_loop_through_a_checkpoint_is_purged_by_the_next_structural_save(/* PEP102.9, I-034 */)
	{
		var x = await Directive(api => api.CreateStandaloneAsync("X", cancellationToken: Ct));
		var unrelated = await Directive(api => api.CreateStandaloneAsync("Unrelated", cancellationToken: Ct));
		var checkpoint = await Deps(api => api.CreateCheckpointAsync("Gate", cancellationToken: Ct));

		// Written straight to the table, as a vault stored them before I-034: X.finish < gate < X.begin.
		var ids = await Vault.QueryAsync(async context =>
		{
			var older = new Dependency { SourceKind = DependencyEndpointKind.Directive, SourceId = x.Id, TargetKind = DependencyEndpointKind.Checkpoint, TargetId = checkpoint.Id };
			var newer = new Dependency { SourceKind = DependencyEndpointKind.Checkpoint, SourceId = checkpoint.Id, TargetKind = DependencyEndpointKind.Directive, TargetId = x.Id };
			context.Dependencies.Add(older);
			await context.SaveChangesAsync(Ct);
			context.Dependencies.Add(newer);
			await context.SaveChangesAsync(Ct);
			return (Older: older.Id, Newer: newer.Id);
		});
		Assert.Equal(2, await Vault.QueryAsync(context => context.Dependencies.CountAsync(Ct)));

		// A status change anywhere in the backlog runs the purge, and with no contradicted edge touching what moved it
		// falls back to every contradicted one: the newer edge of the loop goes, on the record.
		await Directive(api => api.ShiftStellarWorkflowAsync(unrelated.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Active), Ct));

		var remaining = await Vault.QueryAsync(context => context.Dependencies.AsNoTracking().Select(dependency => dependency.Id).ToListAsync(Ct));
		Assert.Equal(ids.Older, Assert.Single(remaining));
		var audit = await Vault.QueryAsync(context => context.AuditLogEntries.AsNoTracking()
			.Where(entry => entry.Action == "dependency.purge")
			.ToListAsync(Ct));
		Assert.Equal(ids.Newer.ToString(System.Globalization.CultureInfo.InvariantCulture), Assert.Single(audit).SubjectId);
	}

	[Fact]
	public async Task Updating_a_checkpoint_changes_its_name_toll_and_condition(/* PEP102 */)
	{
		var checkpoint = await Deps(api => api.CreateCheckpointAsync("Gate", cancellationToken: Ct));

		// Set a name, a toll, and require an unmet condition.
		var set = await Deps(api => api.UpdateCheckpointAsync(checkpoint.Id, new CheckpointUpdate(Title: "Grand Gate", CelestronToll: 12, ExternalCondition: false), Ct));
		Assert.Equal("Grand Gate", set.Title);
		Assert.Equal(12, set.CelestronToll);
		Assert.False(set.ExternalCondition);

		// A negative toll is refused.
		await Assert.ThrowsAsync<InvalidOperationException>(() => Deps(api => api.UpdateCheckpointAsync(checkpoint.Id, new CheckpointUpdate(CelestronToll: -1), Ct)));

		// Clearing removes the toll and the condition; an unnamed update leaves the name alone.
		var cleared = await Deps(api => api.UpdateCheckpointAsync(checkpoint.Id, new CheckpointUpdate(CelestronToll: null, ExternalCondition: null), Ct));
		Assert.Equal("Grand Gate", cleared.Title);
		Assert.Null(cleared.CelestronToll);
		Assert.Null(cleared.ExternalCondition);
	}

	[Fact]
	public async Task A_markdown_sync_keeps_the_sprints_milestone_and_layout(/* PEP102 */)
	{
		const string title = "MilestoneReproSprint";
		var planning = await Onrush(api => api.PlanAsync(new OnrushSprintPlan(title), Ct));
		var begun = await Onrush(api => api.BeginAsync(planning.Id, cancellationToken: Ct));
		var milestoneId = begun.MilestoneCheckpointId!;
		Assert.False(string.IsNullOrWhiteSpace(milestoneId));

		// A saved layout is the other database-only field a frontmatter sync would otherwise wipe.
		await Onrush(api => api.SetGraphLayoutAsync(begun.Id, "{\"n\":{\"x\":3,\"y\":4}}", Ct));

		// Re-sync the sprint's note exactly as the vault watcher would on a file change. Its frontmatter carries
		// neither the milestone id nor the layout, so a naive SetValues would clear both.
		var sprintFile = Vault.MarkdownFilesUnder(Vault.AbsolutePath("")).First(file => file.Contains(title, StringComparison.OrdinalIgnoreCase));
		await Vault.ReconcileAsync(sprintFile);

		var reloaded = await Onrush(api => api.GetAsync(begun.Id, Ct));
		Assert.Equal(milestoneId, reloaded!.MilestoneCheckpointId);
		Assert.Equal("{\"n\":{\"x\":3,\"y\":4}}", reloaded.GraphLayout);
	}

	[Fact]
	public async Task A_sprints_milestone_checkpoint_can_be_edited_and_stays_the_milestone(/* PEP102 */)
	{
		var sprint = await Onrush(api => api.PlanAsync(new OnrushSprintPlan("Sprint"), Ct));
		var milestoneId = sprint.MilestoneCheckpointId!;
		Assert.False(string.IsNullOrWhiteSpace(milestoneId));

		// Editing the milestone is allowed — it is only its deletion that the service refuses.
		var updated = await Deps(api => api.UpdateCheckpointAsync(milestoneId, new CheckpointUpdate(Title: "Grand Finale", CelestronToll: 5), Ct));
		Assert.Equal("Grand Finale", updated.Title);
		Assert.Equal(5, updated.CelestronToll);
		// The edit leaves the two-way binding to its sprint intact: it is still tracked, and still the milestone.
		Assert.Equal(sprint.Id, updated.OnrushSprintId);

		var reloaded = await Onrush(api => api.GetPlanningAsync(Ct));
		Assert.Equal(milestoneId, reloaded!.MilestoneCheckpointId);
		Assert.Contains(reloaded.Checkpoints, checkpoint => checkpoint.Id == milestoneId);
	}

	[Fact]
	public async Task Saving_the_graph_layout_keeps_the_sprints_checkpoints(/* PEP102 */)
	{
		var sprint = await Onrush(api => api.PlanAsync(new OnrushSprintPlan("Sprint"), Ct));
		var checkpoint = await Deps(api => api.CreateCheckpointAsync("Gate", onrushSprintId: sprint.Id, cancellationToken: Ct));

		// A layout save touches only the sprint's own column; the checkpoints it tracks must be undisturbed.
		await Onrush(api => api.SetGraphLayoutAsync(sprint.Id, $"{{\"Checkpoint:{checkpoint.Id}\":{{\"x\":10,\"y\":20}}}}", Ct));

		var reloaded = await Onrush(api => api.GetAsync(sprint.Id, Ct));
		Assert.NotNull(reloaded);
		Assert.Contains(reloaded!.Checkpoints, item => item.Id == checkpoint.Id);
		Assert.Contains(reloaded.Checkpoints, item => item.Id == sprint.MilestoneCheckpointId);
	}

	[Fact]
	public async Task Deleting_an_onrush_takes_its_milestone_and_orders_but_frees_its_members(/* PEP102.5 */)
	{
		var sprint = await Onrush(api => api.PlanAsync(new OnrushSprintPlan("Doomed"), Ct));
		var milestoneId = sprint.MilestoneCheckpointId!;
		var objective = await Objective(api => api.CreateStandaloneAsync("Keep me", cancellationToken: Ct));
		await Objective(api => api.AddToOnrushAsync(objective.Id, sprint.Id, Ct));
		var checkpoint = await Deps(api => api.CreateCheckpointAsync("Gate", onrushSprintId: sprint.Id, cancellationToken: Ct));

		await Onrush(api => api.DeleteAsync(sprint.Id, Ct));

		// The sprint and its milestone are gone (the two-FK cycle between sprint and milestone and all).
		Assert.Null(await Onrush(api => api.GetAsync(sprint.Id, Ct)));
		Assert.Null(await Deps(api => api.GetCheckpointAsync(milestoneId, Ct)));

		// Its members survive as independent entities, merely detached from the onrush.
		var freedObjective = await Objective(api => api.GetAsync(objective.Id, Ct));
		Assert.NotNull(freedObjective);
		Assert.Null(freedObjective!.OnrushSprintId);
		var freedCheckpoint = await Deps(api => api.GetCheckpointAsync(checkpoint.Id, Ct));
		Assert.NotNull(freedCheckpoint);
		Assert.Null(freedCheckpoint!.OnrushSprintId);
	}

	[Fact]
	public async Task Planning_an_onrush_creates_its_milestone_which_cannot_be_deleted_alone(/* PEP102 */)
	{
		var sprint = await Onrush(api => api.PlanAsync(new OnrushSprintPlan("Sprint One"), Ct));
		Assert.False(string.IsNullOrWhiteSpace(sprint.MilestoneCheckpointId));

		// The milestone is a real, tracked checkpoint of the sprint.
		var reloaded = await Onrush(api => api.GetAsync(sprint.Id, Ct));
		Assert.NotNull(reloaded);
		Assert.Equal(sprint.MilestoneCheckpointId, reloaded!.MilestoneCheckpoint?.Id);
		Assert.Contains(reloaded.Checkpoints, checkpoint => checkpoint.Id == sprint.MilestoneCheckpointId);

		// It is bound to the sprint for the sprint's life: it cannot be deleted on its own.
		await Assert.ThrowsAsync<InvalidOperationException>(() => Deps(api => api.DeleteCheckpointAsync(sprint.MilestoneCheckpointId!, Ct)));
	}

	[Fact]
	public async Task Locked_whole_fate_pauses_materialization_until_the_source_finishes()
	{
		var slot = DateOnly.FromDateTime(DateTime.Today).AddDays(30);
		var source = await Directive(api => api.CreateStandaloneAsync("Fate prereq", cancellationToken: Ct));
		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Solstice", Date: slot), Ct));
		var occurrenceRef = new EventiveOccurrenceRef(fate.Id, slot.ToDateTime(TimeOnly.MinValue));

		await Deps(api => api.CreateAsync(DirectiveRef(source.Id), new EndpointRef(DependencyEndpointKind.Fate, fate.Id), cancellationToken: Ct));

		// Interacting with the gated occurrence refuses to resolve it into a hardened row (PEP101).
		await Assert.ThrowsAsync<InvalidOperationException>(() => Declarative(api => api.UpdateEventiveAsync(occurrenceRef, new EventiveUpdate(), Ct)));

		await Directive(api => api.ShiftStellarWorkflowAsync(source.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Fulfilled), Ct));
		var eventive = await Declarative(api => api.UpdateEventiveAsync(occurrenceRef, new EventiveUpdate(), Ct));
		Assert.True(eventive.Id > 0);
	}

	[Fact]
	public async Task Eventive_source_reference_survives_a_move_via_the_recurrence_slot()
	{
		var target = await Directive(api => api.CreateStandaloneAsync("Downstream", cancellationToken: Ct));
		var slot = DateOnly.FromDateTime(DateTime.Today).AddDays(30);
		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Occurrence", Date: slot), Ct));
		var eventive = await Declarative(api => api.UpdateEventiveAsync(new EventiveOccurrenceRef(fate.Id, slot.ToDateTime(TimeOnly.MinValue)), new EventiveUpdate(), Ct));
		Assert.Equal(slot.ToDateTime(TimeOnly.MinValue), eventive.RecurrenceId);

		await Deps(api => api.CreateAsync(
			new EndpointRef(DependencyEndpointKind.Eventive, fate.Id, slot.ToDateTime(TimeOnly.MinValue)),
			DirectiveRef(target.Id),
			cancellationToken: Ct));

		// The future, still-pending occurrence has not finished, so the target is gated; time moves an occurrence, so
		// nothing the core could move would resolve it.
		await Assert.ThrowsAsync<StateEnforcementException>(() => Directive(api =>
			api.ShiftStellarWorkflowAsync(target.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Active), Ct)));

		// Move the occurrence (its current date changes): the reference still matches by slot. Missing it sets it aside, which
		// satisfies nothing (PEP103), so the target stays gated.
		var eventiveRef = new EventiveOccurrenceRef(eventive.RecurrenceOwnerUid, eventive.RecurrenceId);
		await Declarative(api => api.UpdateEventiveAsync(eventiveRef, new EventiveUpdate(Date: slot.AddDays(10)), Ct));
		await Declarative(api => api.UpdateEventiveAsync(eventiveRef, new EventiveUpdate(Resolution: EventiveResolution.Missed), Ct));
		Assert.True((await Deps(api => api.GetLockAsync(target.Id, Ct))).BlockedBegin);

		// Taken back to pending and moved into the past, it has taken place, so it has finished and releases the target.
		var past = DateOnly.FromDateTime(DateTime.Today).AddDays(-2);
		await Declarative(api => api.UpdateEventiveAsync(eventiveRef, new EventiveUpdate(Date: past, Resolution: EventiveResolution.Pending), Ct));

		var openView = await Deps(api => api.GetLockAsync(target.Id, Ct));
		Assert.False(openView.BlockedBegin);
		var begun = await Directive(api => api.ShiftStellarWorkflowAsync(target.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Active), Ct));
		Assert.Equal(DirectiveStatus.Active, begun.Status);
	}

	[Fact]
	public async Task A_served_dependency_says_whether_it_gates_the_targets_next_transition()
	{
		var target = await Objective(api => api.CreateStandaloneAsync("Arch", cancellationToken: Ct));
		var keystone = await Directive(api => api.CreateStandaloneAsync("Keystone", cancellationToken: Ct));
		var capstone = await Directive(api => api.CreateStandaloneAsync("Capstone", cancellationToken: Ct));
		var beginGate = await Deps(api => api.CreateAsync(DirectiveRef(keystone.Id), ObjectiveRef(target.Id), cancellationToken: Ct));
		var finishGate = await Deps(api => api.CreateAsync(DirectiveRef(capstone.Id), ObjectiveRef(target.Id), constraint: DependencyConstraint.ToFinish, cancellationToken: Ct));

		// The objective has not begun: its begin gate holds it back now, its finish gate only later. The create
		// response carries the stamp too.
		Assert.True(beginGate.GatesNextTransition);
		Assert.False(finishGate.GatesNextTransition);
		var served = await Deps(api => api.ListAsync(target.Id, Ct));
		Assert.True(served.Single(dependency => dependency.Id == beginGate.Id).GatesNextTransition);
		Assert.False(served.Single(dependency => dependency.Id == finishGate.Id).GatesNextTransition);

		// Once the keystone is finished and the objective has begun, its finish gate is the one that holds it back.
		await Directive(api => api.ShiftStellarWorkflowAsync(keystone.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Fulfilled), Ct));
		await Objective(api => api.ShiftWorkflowAsync(target.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Polaris), Ct));
		served = await Deps(api => api.ListAsync(target.Id, Ct));
		Assert.False(served.Single(dependency => dependency.Id == beginGate.Id).GatesNextTransition);
		Assert.True(served.Single(dependency => dependency.Id == finishGate.Id).GatesNextTransition);
		var lockView = await Deps(api => api.GetLockAsync(target.Id, Ct));
		Assert.True(Assert.Single(lockView.Unsatisfied).GatesNextTransition);

		// A satisfied gate gates nothing.
		await Directive(api => api.ShiftStellarWorkflowAsync(capstone.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Fulfilled), Ct));
		served = await Deps(api => api.ListAsync(target.Id, Ct));
		Assert.All(served, dependency => Assert.False(dependency.GatesNextTransition));
	}

	[Fact]
	public async Task A_gate_on_a_transition_the_target_already_made_does_not_gate_its_next_one()
	{
		var prerequisite = await Directive(api => api.CreateStandaloneAsync("Late prereq", cancellationToken: Ct));
		var begun = await Objective(api => api.CreateStandaloneAsync("Already running", cancellationToken: Ct));
		await Objective(api => api.ShiftWorkflowAsync(begun.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Polaris), Ct));
		var finished = await Objective(api => api.CreateStandaloneAsync("Already done", cancellationToken: Ct));
		await Objective(api => api.ShiftWorkflowAsync(finished.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Done), Ct));

		// Gates added after the fact stay unsatisfied, but they concern transitions their targets have already made.
		var lateBegin = await Deps(api => api.CreateAsync(DirectiveRef(prerequisite.Id), ObjectiveRef(begun.Id), cancellationToken: Ct));
		var lateFinish = await Deps(api => api.CreateAsync(DirectiveRef(prerequisite.Id), ObjectiveRef(finished.Id), constraint: DependencyConstraint.ToFinish, cancellationToken: Ct));

		var served = await Deps(api => api.ListAsync(prerequisite.Id, Ct));
		Assert.All(served, dependency => Assert.False(dependency.Satisfied));
		Assert.False(served.Single(dependency => dependency.Id == lateBegin.Id).GatesNextTransition);
		Assert.False(served.Single(dependency => dependency.Id == lateFinish.Id).GatesNextTransition);
	}

	[Fact]
	public async Task Every_unsatisfied_gate_into_a_checkpoint_gates_its_unlock()
	{
		var prerequisite = await Directive(api => api.CreateStandaloneAsync("Gate prereq", cancellationToken: Ct));
		var checkpoint = await Deps(api => api.CreateCheckpointAsync("Waypoint", cancellationToken: Ct));
		var gate = await Deps(api => api.CreateAsync(DirectiveRef(prerequisite.Id), CheckpointRef(checkpoint.Id), cancellationToken: Ct));

		// A checkpoint is one-shot, never begun: its next transition is its unlock (its finish), which the gate holds back.
		Assert.True(gate.GatesNextTransition);
		Assert.True((await Deps(api => api.ListAsync(checkpoint.Id, Ct))).Single().GatesNextTransition);
	}

	[Fact]
	public async Task A_directives_finish_depends_intrinsically_on_each_direct_child(/* PEP102.9 */)
	{
		var parent = await Directive(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Ct));
		var child = await Directive(api => api.CreateFromParentAsync(parent.Id, "Strike", cancellationToken: Ct));
		var quest = await Objective(api => api.CreateFromDirectiveAsync(parent.Id, "Quest", cancellationToken: Ct));
		// A grandchild hangs off the subdirective, not the parent: it is the subdirective's edge, never the parent's.
		var sortie = await Objective(api => api.CreateFromDirectiveAsync(child.Id, "Sortie", cancellationToken: Ct));

		// Served, not stored: no identity, an intrinsic origin, finish-to-finish, and a stamp like any other edge.
		var served = await Deps(api => api.ListAsync(parent.Id, Ct));
		var intrinsic = served.Where(dependency => dependency.Origin == DependencyOrigin.Intrinsic).ToList();
		Assert.Equal(2, intrinsic.Count);
		Assert.All(intrinsic, dependency =>
		{
			Assert.Equal(0, dependency.Id);
			Assert.Equal(DirectiveRef(parent.Id), dependency.Target);
			Assert.Equal(DependencyTrigger.OnFinish, dependency.Trigger);
			Assert.Equal(DependencyConstraint.ToFinish, dependency.Constraint);
			Assert.False(dependency.Satisfied);
		});
		Assert.Contains(intrinsic, dependency => dependency.Source == DirectiveRef(child.Id));
		Assert.Contains(intrinsic, dependency => dependency.Source == ObjectiveRef(quest.Id));
		Assert.Empty(await Vault.QueryAsync(context => context.Dependencies.ToListAsync(Ct)));

		// Not begun, so the finish gates do not hold it back now; the lock view still lists them.
		Assert.All(intrinsic, dependency => Assert.False(dependency.GatesNextTransition));
		var lockView = await Deps(api => api.GetLockAsync(parent.Id, Ct));
		Assert.True(lockView.BlockedFinish);
		Assert.Equal(2, lockView.Unsatisfied.Count);

		// The parent cannot finish while a child is open — whichever pathway asks.
		await Directive(api => api.ShiftStellarWorkflowAsync(parent.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Active), Ct));
		var refused = await Assert.ThrowsAsync<StateEnforcementException>(() =>
			Directive(api => api.ShiftStellarWorkflowAsync(parent.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Fulfilled), Ct)));
		Assert.Contains("cannot finish: it still has 2 open children", refused.Message);
		Assert.All(await Deps(api => api.ListAsync(parent.Id, Ct)), dependency => Assert.True(dependency.GatesNextTransition));

		// Archived is the finalised Done: it finishes the objective. The subdirective is held by its own child in turn —
		// the grandchild is the subdirective's edge, not the parent's — and finishes once that one is done.
		await Objective(api => api.ShiftWorkflowAsync(quest.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Archived), Ct));
		Assert.DoesNotContain(await Deps(api => api.ListAsync(parent.Id, Ct)), dependency => dependency.SourceId == sortie.Id);
		await Assert.ThrowsAsync<StateEnforcementException>(() =>
			Directive(api => api.ShiftStellarWorkflowAsync(child.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Fulfilled), Ct)));
		await Objective(api => api.ShiftWorkflowAsync(sortie.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Done), Ct));
		await Directive(api => api.ShiftStellarWorkflowAsync(child.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Fulfilled), Ct));
		Assert.All(await Deps(api => api.ListAsync(parent.Id, Ct)), dependency => Assert.True(dependency.Satisfied));
		await Directive(api => api.ShiftStellarWorkflowAsync(parent.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Fulfilled), Ct));
	}

	[Fact]
	public async Task A_failed_end_switches_its_intrinsic_edge_off_but_still_blocks_what_it_explicitly_blocks(/* PEP102.9 */)
	{
		var parent = await Directive(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Ct));
		var quest = await Objective(api => api.CreateFromDirectiveAsync(parent.Id, "Quest", cancellationToken: Ct));
		var dependant = await Objective(api => api.CreateStandaloneAsync("Afterwards", cancellationToken: Ct));
		var explicitEdge = await Deps(api => api.CreateAsync(ObjectiveRef(quest.Id), ObjectiveRef(dependant.Id), cancellationToken: Ct));

		await Objective(api => api.ShiftWorkflowAsync(quest.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Failed), Ct));

		// Failed is not a finish: the explicit edge stays unmet and holds its dependant's begin (Polaris, PEP101).
		var served = await Deps(api => api.ListAsync(quest.Id, Ct));
		var stored = Assert.Single(served, dependency => dependency.Id == explicitEdge.Id);
		Assert.False(stored.Satisfied);
		var held = await Assert.ThrowsAsync<StateEnforcementException>(() =>
			Interactive(services => services.GetRequiredService<IObjectiveApi>().ShiftWorkflowAsync(dependant.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Polaris), Ct), force: true));
		// Taking a set-aside item up again is the user's call, so even a confirmed shift cannot move it.
		Assert.Equal(StateDecisionOutcome.Refuse, held.Decision.Outcome);
		Assert.False(Assert.Single(held.Decision.Conflicts).IsCompensable);

		// But the intrinsic edge is gone: a failed child neither holds its parent back nor counts as done.
		Assert.DoesNotContain(served, dependency => dependency.Origin == DependencyOrigin.Intrinsic);
		await Directive(api => api.ShiftStellarWorkflowAsync(parent.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Fulfilled), Ct));

		// A failed parent is held back by nothing either.
		var failing = await Directive(api => api.CreateStandaloneAsync("Doomed", cancellationToken: Ct));
		await Objective(api => api.CreateFromDirectiveAsync(failing.Id, "Open", cancellationToken: Ct));
		await Directive(api => api.ShiftStellarWorkflowAsync(failing.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Failed), Ct));
		Assert.Empty(await Deps(api => api.ListAsync(failing.Id, Ct)));
	}

	[Fact]
	public async Task An_explicit_edge_against_the_structure_is_refused_and_one_restating_it_is_a_duplicate(/* PEP102.9 */)
	{
		var parent = await Directive(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Ct));
		var quest = await Objective(api => api.CreateFromDirectiveAsync(parent.Id, "Quest", cancellationToken: Ct));

		// The parent's finish before its own child's begin loops through the intrinsic edge, and the message says so.
		var impossible = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			Deps(api => api.CreateAsync(DirectiveRef(parent.Id), ObjectiveRef(quest.Id), cancellationToken: Ct)));
		Assert.Contains("logically impossible", impossible.Message);
		Assert.Contains("(intrinsic)", impossible.Message);

		// The parent's begin before the child's begin is the natural order and is allowed.
		var natural = await Deps(api => api.CreateAsync(DirectiveRef(parent.Id), ObjectiveRef(quest.Id), DependencyTrigger.OnBegin, DependencyConstraint.ToBegin, Ct));
		Assert.True(natural.Id > 0);

		// Restating the intrinsic edge is a duplicate of something the structure already says.
		var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			Deps(api => api.CreateAsync(ObjectiveRef(quest.Id), DirectiveRef(parent.Id), constraint: DependencyConstraint.ToFinish, cancellationToken: Ct)));
		Assert.Contains("already implied", duplicate.Message);
	}

	[Fact]
	public async Task Moving_an_entity_purges_the_explicit_edges_the_structure_now_contradicts(/* PEP102.9 */)
	{
		var alpha = await Directive(api => api.CreateStandaloneAsync("Alpha", cancellationToken: Ct));
		var beta = await Directive(api => api.CreateStandaloneAsync("Beta", cancellationToken: Ct));
		var quest = await Objective(api => api.CreateFromDirectiveAsync(alpha.Id, "Quest", cancellationToken: Ct));
		var bystander = await Objective(api => api.CreateStandaloneAsync("Bystander", cancellationToken: Ct));

		// Beta's finish before the quest's begin is fine while the quest belongs to Alpha; the bystander's edge has
		// nothing to do with either directive.
		var doomed = await Deps(api => api.CreateAsync(DirectiveRef(beta.Id), ObjectiveRef(quest.Id), cancellationToken: Ct));
		var kept = await Deps(api => api.CreateAsync(ObjectiveRef(bystander.Id), ObjectiveRef(quest.Id), cancellationToken: Ct));

		// Moving the quest under Beta closes the loop: the edge touching the moved objective gives way, silently but
		// on the record; the unrelated edge stays.
		await Objective(api => api.UpdateAsync(quest.Id, new ObjectiveUpdate(DirectiveId: beta.Id), Ct));

		var remaining = await Vault.QueryAsync(context => context.Dependencies.AsNoTracking().ToListAsync(Ct));
		Assert.Equal(kept.Id, Assert.Single(remaining).Id);
		var audit = await Vault.QueryAsync(context => context.AuditLogEntries.AsNoTracking()
			.Where(entry => entry.Action == "dependency.purge")
			.ToListAsync(Ct));
		var entry = Assert.Single(audit);
		Assert.Equal(doomed.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), entry.SubjectId);
		Assert.Contains("contradicted", entry.DetailsJson);
	}

	[Fact]
	public async Task Over_means_over_until_open_work_arrives(/* PEP102.9, PEP103 patches 4 and 5 */)
	{
		var directive = await Directive(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Ct));
		await Directive(api => api.ShiftStellarWorkflowAsync(directive.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Fulfilled), Ct));

		// What is done is done: open work under a directive that is only Fulfilled is accepted, and keeps it from being Over.
		var early = await Objective(api => api.CreateFromDirectiveAsync(directive.Id, "Early quest", cancellationToken: Ct));
		Assert.Equal(DirectiveStatus.Fulfilled, await StatusOfAsync(directive.Id));
		var notYet = await Assert.ThrowsAsync<StateEnforcementException>(() =>
			Directive(api => api.ShiftStellarWorkflowAsync(directive.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Over), Ct)));
		Assert.Contains("cannot be Over: it still has 1 open child", notYet.Message);
		await Objective(api => api.ShiftWorkflowAsync(early.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Done), Ct));
		await Directive(api => api.ShiftStellarWorkflowAsync(directive.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Over), Ct));
		Assert.Contains("status: Over", Vault.ReadVaultFile("Directives/Campaign/Campaign.md"));

		// New work under an Over directive steps it back to Fulfilled, which is a compensation: under Warn, with nobody to
		// ask, it is refused.
		var refused = await Assert.ThrowsAsync<StateEnforcementException>(() =>
			Objective(api => api.CreateFromDirectiveAsync(directive.Id, "Quest", cancellationToken: Ct)));
		Assert.Equal(StateDecisionOutcome.Refuse, refused.Decision.Outcome);
		Assert.Equal(StateJudge.ClosedRule, Assert.Single(refused.Decision.Conflicts).Rule);
		Assert.Contains("its directive is Over", refused.Message);
		Assert.Equal(DirectiveStatus.Over, await StatusOfAsync(directive.Id));

		// A client is asked first, and told which item going ahead steps back; confirming creates the work and returns the
		// directive to Fulfilled — row and note alike.
		var held = await Assert.ThrowsAsync<StateEnforcementException>(() =>
			Interactive(services => services.GetRequiredService<IObjectiveApi>().CreateFromDirectiveAsync(directive.Id, "Quest", cancellationToken: Ct)));
		Assert.Equal(StateDecisionOutcome.Confirm, held.Decision.Outcome);
		var unclose = Assert.Single(held.Decision.Compensations);
		Assert.Equal(directive.Id, unclose.Subject.Id);
		Assert.Equal(nameof(DirectiveStatus.Fulfilled), unclose.Status);
		Assert.Contains("return 1 item to Fulfilled", held.Message);
		var quest = await Interactive(services => services.GetRequiredService<IObjectiveApi>().CreateFromDirectiveAsync(directive.Id, "Quest", cancellationToken: Ct), force: true);
		Assert.Equal(DirectiveStatus.Fulfilled, await StatusOfAsync(directive.Id));
		Assert.Contains("status: Fulfilled", Vault.ReadVaultFile("Directives/Campaign/Campaign.md"));

		// A closed directive relies on every child having finished, so being Over again needs the child finished.
		var stillOpen = await Assert.ThrowsAsync<StateEnforcementException>(() =>
			Directive(api => api.ShiftStellarWorkflowAsync(directive.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Over), Ct)));
		Assert.Contains("cannot be Over: it still has 1 open child", stillOpen.Message);
		await Objective(api => api.ShiftWorkflowAsync(quest.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Done), Ct));
		await Directive(api => api.ShiftStellarWorkflowAsync(directive.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Over), Ct));

		// Under Auto the core acts on its own: a finished child brought back reopens what relied on its finish (PEP103 patch 4)…
		await SetEnforcementAsync(BacklogStateEnforcementLevel.Auto);
		await Objective(api => api.ShiftWorkflowAsync(quest.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Standby), Ct));
		Assert.Equal(DirectiveStatus.Active, await StatusOfAsync(directive.Id));
		await Objective(api => api.ShiftWorkflowAsync(quest.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Done), Ct));
		await Directive(api => api.ShiftStellarWorkflowAsync(directive.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Over), Ct));

		// …a child set aside is not open work, but taking it up again is (I-032), and only steps the directive back to Fulfilled.
		await Objective(api => api.ShiftWorkflowAsync(quest.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Failed), Ct));
		Assert.Equal(DirectiveStatus.Over, await StatusOfAsync(directive.Id));
		await Objective(api => api.ShiftWorkflowAsync(quest.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Standby), Ct));
		Assert.Equal(DirectiveStatus.Fulfilled, await StatusOfAsync(directive.Id));
		await Objective(api => api.ShiftWorkflowAsync(quest.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Done), Ct));
		await Directive(api => api.ShiftStellarWorkflowAsync(directive.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Over), Ct));

		// And so does an open child moved in from elsewhere.
		var stray = await Objective(api => api.CreateStandaloneAsync("Stray", cancellationToken: Ct));
		await Objective(api => api.UpdateAsync(stray.Id, new ObjectiveUpdate(DirectiveId: directive.Id), Ct));
		Assert.Equal(DirectiveStatus.Fulfilled, await StatusOfAsync(directive.Id));
	}

	[Fact]
	public async Task Deleting_an_endpoint_releases_the_explicit_edges_that_named_it(/* PEP102.9 */)
	{
		var gone = await Objective(api => api.CreateStandaloneAsync("Gone", cancellationToken: Ct));
		var stays = await Objective(api => api.CreateStandaloneAsync("Stays", cancellationToken: Ct));
		var other = await Objective(api => api.CreateStandaloneAsync("Other", cancellationToken: Ct));
		// One edge out of the doomed objective, one into it, and one that never names it.
		await Deps(api => api.CreateAsync(ObjectiveRef(gone.Id), ObjectiveRef(stays.Id), cancellationToken: Ct));
		await Deps(api => api.CreateAsync(ObjectiveRef(other.Id), ObjectiveRef(gone.Id), cancellationToken: Ct));
		var unrelated = await Deps(api => api.CreateAsync(ObjectiveRef(other.Id), ObjectiveRef(stays.Id), cancellationToken: Ct));

		await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>().DeleteAsync(gone.Id, Ct));

		var remaining = await Vault.QueryAsync(context => context.Dependencies.AsNoTracking().ToListAsync(Ct));
		Assert.Equal(unrelated.Id, Assert.Single(remaining).Id);
	}

	private Task<DirectiveStatus> StatusOfAsync(string directiveId)
		=> Vault.QueryAsync(context => context.Directives.OfType<StellarDirective>().AsNoTracking().IgnoreAutoIncludes()
			.Where(directive => directive.Id == directiveId)
			.Select(directive => directive.Status)
			.SingleAsync(Ct));

	private static EndpointRef ObjectiveRef(string id) => new(DependencyEndpointKind.Objective, id);

	private Task<int> BankedAsync() => Vault.WithScopeAsync(services => services.GetRequiredService<PlaintorchStateService>().GetCelestronBankedAsync(Ct));
}
