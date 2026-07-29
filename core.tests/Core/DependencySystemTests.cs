using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
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

	private static EndpointRef DirectiveRef(string id) => new(DependencyEndpointKind.Directive, id);
	private static EndpointRef CheckpointRef(string id) => new(DependencyEndpointKind.Checkpoint, id);

	[Fact]
	public async Task Finish_triggered_begin_constraint_gates_the_target()
	{
		var source = await Directive(api => api.CreateStandaloneAsync("Foundation", cancellationToken: Ct));
		var target = await Directive(api => api.CreateStandaloneAsync("Tower", cancellationToken: Ct));

		await Deps(api => api.CreateAsync(DirectiveRef(source.Id), DirectiveRef(target.Id), cancellationToken: Ct));

		// Target cannot begin while the source is unfinished; the lock is emitted but the status is untouched.
		await Assert.ThrowsAsync<InvalidOperationException>(() => Directive(api =>
			api.ShiftStellarWorkflowAsync(target.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Active), Ct)));
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

		await Assert.ThrowsAsync<InvalidOperationException>(() => Objective(api =>
			api.ShiftWorkflowAsync(objective.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Onrush), Ct)));

		await Directive(api => api.ShiftStellarWorkflowAsync(source.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Over), Ct));
		var promoted = await Objective(api => api.ShiftWorkflowAsync(objective.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Onrush), Ct));
		Assert.Equal(ObjectiveStatus.Onrush, promoted.Status);
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

		// A checkpoint source has no begin/finish, so it may not carry a trigger.
		await Assert.ThrowsAsync<InvalidOperationException>(() => Deps(api => api.CreateAsync(CheckpointRef(checkpoint.Id), DirectiveRef(b.Id), trigger: DependencyTrigger.OnFinish, cancellationToken: Ct)));

		// Lunar directives are excluded.
		await Assert.ThrowsAsync<InvalidOperationException>(() => Deps(api => api.CreateAsync(DirectiveRef(lunar.Id), DirectiveRef(b.Id), cancellationToken: Ct)));
	}

	[Fact]
	public async Task Locked_whole_fate_pauses_materialization_until_the_source_finishes()
	{
		var source = await Directive(api => api.CreateStandaloneAsync("Fate prereq", cancellationToken: Ct));
		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Solstice", Date: DateOnly.FromDateTime(DateTime.Today).AddDays(30)), Ct));

		await Deps(api => api.CreateAsync(DirectiveRef(source.Id), new EndpointRef(DependencyEndpointKind.Fate, fate.Id), cancellationToken: Ct));

		await Assert.ThrowsAsync<InvalidOperationException>(() => Declarative(api => api.MaterializeEventiveAsync(fate.Id, new EventiveMaterialization(), Ct)));

		await Directive(api => api.ShiftStellarWorkflowAsync(source.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Fulfilled), Ct));
		var eventive = await Declarative(api => api.MaterializeEventiveAsync(fate.Id, new EventiveMaterialization(), Ct));
		Assert.True(eventive.Id > 0);
	}

	[Fact]
	public async Task Eventive_source_reference_survives_a_move_via_the_recurrence_slot()
	{
		var target = await Directive(api => api.CreateStandaloneAsync("Downstream", cancellationToken: Ct));
		var slot = DateOnly.FromDateTime(DateTime.Today).AddDays(30);
		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Occurrence", Date: slot), Ct));
		var eventive = await Declarative(api => api.MaterializeEventiveAsync(fate.Id, new EventiveMaterialization(), Ct));
		Assert.Equal(slot, eventive.RecurrenceDate);

		await Deps(api => api.CreateAsync(
			new EndpointRef(DependencyEndpointKind.Eventive, fate.Id, slot),
			DirectiveRef(target.Id),
			cancellationToken: Ct));

		// The future, still-pending occurrence has not finished, so the target is gated.
		await Assert.ThrowsAsync<InvalidOperationException>(() => Directive(api =>
			api.ShiftStellarWorkflowAsync(target.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Active), Ct)));

		// Move the occurrence (its current date changes) then resolve it: the reference still matches by slot.
		await Declarative(api => api.UpdateEventiveAsync(eventive.Id, new EventiveUpdate(Date: slot.AddDays(10)), Ct));
		await Declarative(api => api.UpdateEventiveAsync(eventive.Id, new EventiveUpdate(Resolution: EventiveResolution.Missed), Ct));

		var openView = await Deps(api => api.GetLockAsync(target.Id, Ct));
		Assert.False(openView.BlockedBegin);
		var begun = await Directive(api => api.ShiftStellarWorkflowAsync(target.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Active), Ct));
		Assert.Equal(DirectiveStatus.Active, begun.Status);
	}

	private Task<int> BankedAsync() => Vault.WithScopeAsync(services => services.GetRequiredService<PlaintorchStateService>().GetCelestronBankedAsync(Ct));
}
