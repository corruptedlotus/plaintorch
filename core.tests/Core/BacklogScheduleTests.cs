using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Changes;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Hosting;
using Pleiades.Puck;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// PEP101 backlog schedules, on PEP103's lifecycle states: an incentive, a directive or a checkpoint holds planned starts and
/// finishes, and records of the states it reached. Records are written by the save hook for every state but Open — an
/// objective starts when it is set to Polaris, not when it joins an onrush; a checkpoint never starts and finishes by
/// unlocking — and withdrawn when the item is pulled back along the main line. Setting an item aside keeps them and adds a
/// Deferred record. A schedule added to an occupied slot (the same owner, type and state) replaces the one there, whatever
/// the write pathway. Only plans are set or removed by hand, and schedules go with their owner.
/// </summary>
public sealed class BacklogScheduleTests : VaultTestBase
{
	private CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> Objective<T>(Func<IObjectiveApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IObjectiveApi>()));
	private Task Objective(Func<IObjectiveApi, Task> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IObjectiveApi>()));
	private Task<T> Directive<T>(Func<IDirectiveApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDirectiveApi>()));
	private Task Directive(Func<IDirectiveApi, Task> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDirectiveApi>()));
	private Task<T> Declarative<T>(Func<IDeclarativeApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDeclarativeApi>()));
	private Task<T> Deps<T>(Func<IDependencyApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDependencyApi>()));
	private Task Deps(Func<IDependencyApi, Task> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDependencyApi>()));
	private Task<T> Polaris<T>(Func<IPolarisCycleApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IPolarisCycleApi>()));
	private Task<T> Schedules<T>(Func<IBacklogScheduleApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IBacklogScheduleApi>()));
	private Task Schedules(Func<IBacklogScheduleApi, Task> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IBacklogScheduleApi>()));

	private Task<Objective> ShiftAsync(string objectiveId, ObjectiveStatus status)
		=> Objective(api => api.ShiftWorkflowAsync(objectiveId, new ObjectiveWorkflowShift(status), Ct));

	/// <summary>Reads every schedule of an owner straight from the database, oldest row first.</summary>
	private Task<List<BacklogSchedule>> StoredAsync(string ownerId) => Vault.QueryAsync(context => context.BacklogSchedules
		.AsNoTracking()
		.Where(schedule => schedule.IncentiveId == ownerId || schedule.DirectiveId == ownerId || schedule.CheckpointId == ownerId)
		.OrderBy(schedule => schedule.Id)
		.ToListAsync(Ct));

	private static BacklogSchedule? Slot(IEnumerable<BacklogSchedule> schedules, BacklogScheduleType type, LifecycleState state)
		=> schedules.SingleOrDefault(schedule => schedule.Type == type && schedule.State == state);

	private static BacklogSchedule Record(IEnumerable<BacklogSchedule> schedules, LifecycleState state)
		=> Assert.Single(schedules, schedule => schedule.Type == BacklogScheduleType.Record && schedule.State == state);

	private static EndpointRef DirectiveRef(string id) => new(DependencyEndpointKind.Directive, id);

	private static EndpointRef CheckpointRef(string id) => new(DependencyEndpointKind.Checkpoint, id);

	// --- recording: the objective begins at Polaris ---

	[Fact]
	public async Task An_objective_begins_when_it_is_set_to_polaris_not_when_it_comes_on_board_an_onrush()
	{
		var objective = await Objective(api => api.CreateStandaloneAsync("Chart the course", cancellationToken: Ct));
		Assert.Empty(await StoredAsync(objective.Id));

		// Onboard commits the objective to a sprint without beginning it.
		await ShiftAsync(objective.Id, ObjectiveStatus.Onboard);
		Assert.Empty(await StoredAsync(objective.Id));

		var before = DateTime.Now;
		await ShiftAsync(objective.Id, ObjectiveStatus.Polaris);
		var after = DateTime.Now;

		var begin = Record(await StoredAsync(objective.Id), LifecycleState.Started);
		Assert.Equal(objective.Id, begin.IncentiveId);
		Assert.Null(begin.DirectiveId);
		Assert.Null(begin.CheckpointId);
		Assert.InRange(begin.Time, before, after);
		Assert.Single(await StoredAsync(objective.Id));
	}

	[Fact]
	public async Task Adding_an_onrush_member_records_nothing()
	{
		var objective = await Objective(api => api.CreateStandaloneAsync("Sprint work", cancellationToken: Ct));
		var sprint = await Vault.WithScopeAsync(services => services.GetRequiredService<IOnrushSprintApi>().StartNewAsync(cancellationToken: Ct));

		// Coming on board the active sprint makes a standby objective Onboard, which is not a begin.
		var member = await Objective(api => api.AddToOnrushAsync(objective.Id, sprint.Id, Ct));
		Assert.Equal(ObjectiveStatus.Onboard, member.Status);
		Assert.Empty(await StoredAsync(objective.Id));
	}

	[Fact]
	public async Task Planning_an_objective_into_a_polaris_cycle_records_its_begin()
	{
		var objective = await Objective(api => api.CreateStandaloneAsync("Planned work", cancellationToken: Ct));

		var fromObjective = await Polaris(api => api.PlanExecutiveAsync(
			new PolarisExecutivePlan(PolarisExecutivePlanningMode.FromObjective, ObjectiveId: objective.Id), cancellationToken: Ct));
		Assert.Equal(ObjectiveStatus.Polaris, fromObjective.Objective!.Status);
		Record(await StoredAsync(objective.Id), LifecycleState.Started);

		// An objective created straight into the cycle is created begun, which counts as beginning.
		var standalone = await Polaris(api => api.PlanExecutiveAsync(
			new PolarisExecutivePlan(PolarisExecutivePlanningMode.Standalone, Title: "Fresh work"), cancellationToken: Ct));
		Record(await StoredAsync(standalone.Objective!.Id), LifecycleState.Started);
	}

	[Fact]
	public async Task Finishing_records_the_finish_and_a_jump_straight_to_done_records_both_at_once()
	{
		var worked = await Objective(api => api.CreateStandaloneAsync("Worked", cancellationToken: Ct));
		await ShiftAsync(worked.Id, ObjectiveStatus.Polaris);
		await ShiftAsync(worked.Id, ObjectiveStatus.Done);

		var workedSchedules = await StoredAsync(worked.Id);
		Assert.Equal(2, workedSchedules.Count);
		Assert.True(Record(workedSchedules, LifecycleState.Started).Time <= Record(workedSchedules, LifecycleState.Finished).Time);

		// A finish implies a begin (the reading the dependency system gates on), so skipping Polaris records both, at the
		// same moment.
		var skipped = await Objective(api => api.CreateStandaloneAsync("Skipped", cancellationToken: Ct));
		await ShiftAsync(skipped.Id, ObjectiveStatus.Done);

		var skippedSchedules = await StoredAsync(skipped.Id);
		Assert.Equal(2, skippedSchedules.Count);
		Assert.Equal(Record(skippedSchedules, LifecycleState.Started).Time, Record(skippedSchedules, LifecycleState.Finished).Time);

		// Moving between finished statuses is no new finish.
		var finish = Record(skippedSchedules, LifecycleState.Finished);
		await ShiftAsync(skipped.Id, ObjectiveStatus.Archived);
		Assert.Equal(finish.Id, Record(await StoredAsync(skipped.Id), LifecycleState.Finished).Id);
	}

	[Fact]
	public async Task Pulling_a_status_back_withdraws_the_record_of_the_phase_it_leaves()
	{
		var objective = await Objective(api => api.CreateStandaloneAsync("Round trip", cancellationToken: Ct));
		await ShiftAsync(objective.Id, ObjectiveStatus.Polaris);
		var firstBegin = Record(await StoredAsync(objective.Id), LifecycleState.Started);

		// Un-beginning withdraws the begin record.
		await ShiftAsync(objective.Id, ObjectiveStatus.Standby);
		Assert.Empty(await StoredAsync(objective.Id));

		// Beginning again records afresh.
		await ShiftAsync(objective.Id, ObjectiveStatus.Polaris);
		var secondBegin = Record(await StoredAsync(objective.Id), LifecycleState.Started);
		Assert.NotEqual(firstBegin.Id, secondBegin.Id);
		Assert.True(secondBegin.Time >= firstBegin.Time);

		// Un-finishing withdraws only the finish record: reopened into Polaris, the objective is still begun, and its begin
		// record stands as it was.
		await ShiftAsync(objective.Id, ObjectiveStatus.Done);
		Record(await StoredAsync(objective.Id), LifecycleState.Finished);
		await ShiftAsync(objective.Id, ObjectiveStatus.Polaris);
		Assert.Equal(secondBegin.Id, Assert.Single(await StoredAsync(objective.Id)).Id);

		// Pulling a finished objective all the way back withdraws both.
		await ShiftAsync(objective.Id, ObjectiveStatus.Done);
		Assert.Equal(2, (await StoredAsync(objective.Id)).Count);
		await ShiftAsync(objective.Id, ObjectiveStatus.Standby);
		Assert.Empty(await StoredAsync(objective.Id));
	}

	[Fact]
	public async Task Blocking_a_begun_objective_un_begins_it()
	{
		// Blocked is Open: a blocked objective is meant to go on, but it is not under way. The gates read it that way, and
		// so does its record.
		var objective = await Objective(api => api.CreateStandaloneAsync("Stalled", cancellationToken: Ct));
		await ShiftAsync(objective.Id, ObjectiveStatus.Polaris);
		Record(await StoredAsync(objective.Id), LifecycleState.Started);

		await ShiftAsync(objective.Id, ObjectiveStatus.Blocked);
		Assert.Empty(await StoredAsync(objective.Id));
	}

	[Fact]
	public async Task Failing_sets_an_item_aside_and_keeps_the_records_of_how_far_it_got()
	{
		// Failed is Deferred: off the main line, neither started nor finished, so a failed prerequisite keeps blocking what
		// it gates. Its place on the main line is kept, and with it the records of how far it got.
		var objective = await Objective(api => api.CreateStandaloneAsync("Lost cause", cancellationToken: Ct));
		await ShiftAsync(objective.Id, ObjectiveStatus.Polaris);
		var started = Record(await StoredAsync(objective.Id), LifecycleState.Started);

		await ShiftAsync(objective.Id, ObjectiveStatus.Failed);
		var failed = await StoredAsync(objective.Id);
		Assert.Equal(2, failed.Count);
		Assert.Equal(started.Id, Record(failed, LifecycleState.Started).Id);
		Record(failed, LifecycleState.Deferred);

		// Coming back to Done withdraws the Deferred record and records only the finish: the start it made stands.
		await ShiftAsync(objective.Id, ObjectiveStatus.Done);
		var done = await StoredAsync(objective.Id);
		Assert.Equal(2, done.Count);
		Assert.Equal(started.Id, Record(done, LifecycleState.Started).Id);
		Record(done, LifecycleState.Finished);

		// Failing a finished item keeps both; pulling it back to Standby from there withdraws all three.
		await ShiftAsync(objective.Id, ObjectiveStatus.Failed);
		Assert.Equal([LifecycleState.Deferred, LifecycleState.Started, LifecycleState.Finished], (await StoredAsync(objective.Id)).Select(schedule => schedule.State).Order());
		await ShiftAsync(objective.Id, ObjectiveStatus.Standby);
		Assert.Empty(await StoredAsync(objective.Id));
	}

	[Fact]
	public async Task A_status_edited_in_the_note_is_recorded_like_an_api_shift()
	{
		// The recorder rides the save hook, so a frontmatter edit synced by the watcher records the begin too.
		var objective = await Vault.SeedStandaloneObjectiveAsync("Ship it");
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);
		const string note = "Objectives/Ship it.md";
		var markdown = Vault.ReadVaultFile(note);
		Assert.Contains("status: Standby", markdown);

		Vault.WriteVaultFile(note, markdown.Replace("status: Standby", "status: Polaris", StringComparison.Ordinal));
		await Vault.ReconcileAsync(Vault.AbsolutePath(note));

		var synced = await Objective(api => api.GetAsync(objective.Id, Ct));
		Assert.Equal(ObjectiveStatus.Polaris, synced!.Status);
		Record(await StoredAsync(objective.Id), LifecycleState.Started);
	}

	// --- recording: one interceptor, every save ---

	[Fact]
	public async Task A_synchronous_save_is_recorded_and_settled_like_any_other()
	{
		var objective = await Objective(api => api.CreateStandaloneAsync("Synchronous", cancellationToken: Ct));

		await Vault.WithScopeAsync(services =>
		{
			var context = services.GetRequiredService<PlainfraContext>();
			context.Objectives.Single(item => item.Id == objective.Id).Status = ObjectiveStatus.Polaris;
			context.BacklogSchedules.Add(new BacklogSchedule { IncentiveId = objective.Id, Type = BacklogScheduleType.Plan, State = LifecycleState.Finished, Time = new DateTime(2026, 10, 1) });
			context.SaveChanges();
			context.BacklogSchedules.Add(new BacklogSchedule { IncentiveId = objective.Id, Type = BacklogScheduleType.Plan, State = LifecycleState.Finished, Time = new DateTime(2026, 10, 2) });
			context.SaveChanges();
			return Task.CompletedTask;
		});

		var schedules = await StoredAsync(objective.Id);
		Record(schedules, LifecycleState.Started);
		Assert.Equal(new DateTime(2026, 10, 2), Slot(schedules, BacklogScheduleType.Plan, LifecycleState.Finished)!.Time);
		Assert.Equal(2, schedules.Count);
	}

	[Fact]
	public async Task Minting_an_id_mid_operation_leaves_the_change_to_the_callers_own_save()
	{
		// PUCK issuance registers its id on a context of its own, so a change the caller has pending is not saved as a side
		// effect of minting; the caller's own save stores it, through every save hook, and records it.
		var objective = await Objective(api => api.CreateStandaloneAsync("Pending", cancellationToken: Ct));

		await Vault.WithScopeAsync(async services =>
		{
			var context = services.GetRequiredService<PlainfraContext>();
			var tracked = await context.Objectives.SingleAsync(item => item.Id == objective.Id, Ct);
			tracked.Status = ObjectiveStatus.Polaris;
			services.GetRequiredService<PuckCreationService>().CreateIdFor<Checkpoint>();
			Assert.Equal(EntityState.Modified, context.Entry(tracked).State);
			Assert.Empty(await StoredAsync(objective.Id));
			await context.SaveChangesAsync(Ct);
		});

		Record(await StoredAsync(objective.Id), LifecycleState.Started);
	}

	[Fact]
	public async Task A_status_set_on_an_attached_copy_is_compared_with_what_is_stored()
	{
		// An entity attached rather than loaded carries no original values of its own, so the transition is judged against
		// the stored row.
		var objective = await Objective(api => api.CreateStandaloneAsync("Attached", cancellationToken: Ct));

		await Vault.WithScopeAsync(async services =>
		{
			var context = services.GetRequiredService<PlainfraContext>();
			context.Objectives.Update(new Objective { Id = objective.Id, Title = objective.Title, Status = ObjectiveStatus.Done });
			await context.SaveChangesAsync(Ct);
		});

		var schedules = await StoredAsync(objective.Id);
		Assert.Equal(Record(schedules, LifecycleState.Started).Time, Record(schedules, LifecycleState.Finished).Time);
	}

	// --- recording: the other kinds ---

	[Fact]
	public async Task A_stellar_directive_records_its_begin_and_finish()
	{
		var directive = await Directive(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Ct));
		await Directive(api => api.ShiftStellarWorkflowAsync(directive.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Committed), Ct));
		Assert.Empty(await StoredAsync(directive.Id));

		await Directive(api => api.ShiftStellarWorkflowAsync(directive.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Active), Ct));
		Assert.Equal(directive.Id, Record(await StoredAsync(directive.Id), LifecycleState.Started).DirectiveId);

		await Directive(api => api.ShiftStellarWorkflowAsync(directive.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Fulfilled), Ct));
		var schedules = await StoredAsync(directive.Id);
		Assert.Equal(2, schedules.Count);
		Assert.Equal(directive.Id, Record(schedules, LifecycleState.Finished).DirectiveId);

		// Reactivated, it is no longer finished; back to planned, no longer begun.
		await Directive(api => api.ShiftStellarWorkflowAsync(directive.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Active), Ct));
		var active = Assert.Single(await StoredAsync(directive.Id));
		Assert.Equal((BacklogScheduleType.Record, LifecycleState.Started), (active.Type, active.State));
		await Directive(api => api.ShiftStellarWorkflowAsync(directive.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Planned), Ct));
		Assert.Empty(await StoredAsync(directive.Id));
	}

	[Fact]
	public async Task A_fate_is_started_while_active_and_set_aside_while_stopped()
	{
		// An active fate is Started, so it is recorded as starting when it is created active.
		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Solstice", Date: new DateOnly(2026, 12, 21)), Ct));
		var started = Record(await StoredAsync(fate.Id), LifecycleState.Started);

		// Stopped is Deferred, not a finish: a fate never finishes. The start stands beside the deferral.
		await Declarative(api => api.UpdateFateAsync(fate.Id, new FateUpdate(Status: FateStatus.Stopped), Ct));
		var stopped = await StoredAsync(fate.Id);
		Assert.Equal(2, stopped.Count);
		Record(stopped, LifecycleState.Deferred);

		// Restored, it starts again: a fate keeps a history, so the new start is recorded beside the first one and the
		// deferral (PEP103 patch 6).
		await Declarative(api => api.UpdateFateAsync(fate.Id, new FateUpdate(Status: FateStatus.Active), Ct));
		var restored = await StoredAsync(fate.Id);
		Assert.Equal(3, restored.Count);
		Assert.Contains(restored, schedule => schedule.Id == started.Id);
		Assert.Equal(2, restored.Count(schedule => schedule.State == LifecycleState.Started));
	}

	[Fact]
	public async Task Decrees_and_lunar_directives_record_their_starts_and_deferrals()
	{
		// A lunar directive is created Active, already under way (PEP103 patch 6).
		var lunar = await Directive(api => api.CreateLunarAsync("Moon law", cancellationToken: Ct));
		var started = Record(await StoredAsync(lunar.Id), LifecycleState.Started);

		// On Hold sets it aside, and Stale finishes it; each state it enters is recorded, and none is withdrawn.
		await Directive(api => api.ShiftLunarWorkflowAsync(lunar.Id, new LunarDirectiveWorkflowShift(LunarDirectiveStatus.OnHold), Ct));
		await Directive(api => api.ShiftLunarWorkflowAsync(lunar.Id, new LunarDirectiveWorkflowShift(LunarDirectiveStatus.Stale), Ct));
		var stale = await StoredAsync(lunar.Id);
		Assert.Equal(started.Id, Record(stale, LifecycleState.Started).Id);
		Record(stale, LifecycleState.Deferred);
		Record(stale, LifecycleState.Finished);

		// A decree is created Active, so Started; abandoning it adds the deferral beside the start.
		var decree = await Declarative(api => api.CreateDecreeAsync(new DecreePlan("Routine"), Ct));
		Record(await StoredAsync(decree.Id), LifecycleState.Started);
		await Declarative(api => api.UpdateDecreeAsync(decree.Id, new DecreeUpdate(Status: DecreeStatus.Abandoned), Ct));
		Assert.Equal([LifecycleState.Deferred, LifecycleState.Started], (await StoredAsync(decree.Id)).Select(schedule => schedule.State).Order());
	}

	// --- recording: the one-shot checkpoint ---

	[Fact]
	public async Task A_checkpoint_finishes_when_it_unlocks_and_is_un_finished_when_it_re_locks()
	{
		// With nothing to wait for, a new checkpoint is not finished (PEP103 patch 5, I-036): it records nothing.
		var checkpoint = await Deps(api => api.CreateCheckpointAsync("Gate", cancellationToken: Ct));
		Assert.False(checkpoint.Unlocked);
		Assert.Empty(await StoredAsync(checkpoint.Id));

		// A gate into it gives it something to wait for; meeting the gate unlocks it: finished, and never begun, since a
		// checkpoint cannot be started.
		var first = await Directive(api => api.CreateStandaloneAsync("First prereq", cancellationToken: Ct));
		await Deps(api => api.CreateAsync(DirectiveRef(first.Id), CheckpointRef(checkpoint.Id), cancellationToken: Ct));
		Assert.False((await Deps(api => api.GetCheckpointAsync(checkpoint.Id, Ct)))!.Unlocked);
		Assert.Empty(await StoredAsync(checkpoint.Id));
		await Directive(api => api.ShiftStellarWorkflowAsync(first.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Fulfilled), Ct));
		var unlocked = Assert.Single(await StoredAsync(checkpoint.Id));
		Assert.Equal(BacklogScheduleType.Record, unlocked.Type);
		Assert.Equal(LifecycleState.Finished, unlocked.State);
		Assert.Equal(checkpoint.Id, unlocked.CheckpointId);

		// Another unmet gate re-locks it, which withdraws the finish; meeting that one too finishes it once more.
		var second = await Directive(api => api.CreateStandaloneAsync("Second prereq", cancellationToken: Ct));
		await Deps(api => api.CreateAsync(DirectiveRef(second.Id), CheckpointRef(checkpoint.Id), cancellationToken: Ct));
		Assert.Empty(await StoredAsync(checkpoint.Id));
		await Directive(api => api.ShiftStellarWorkflowAsync(second.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Over), Ct));
		Assert.NotEqual(unlocked.Id, Record(await StoredAsync(checkpoint.Id), LifecycleState.Finished).Id);
	}

	[Fact]
	public async Task A_checkpoint_with_no_edge_anywhere_finishes_on_its_own_condition()
	{
		// No dependency edge exists in the vault at all; the checkpoint's unlock — its finish — still follows its own
		// external condition.
		var checkpoint = await Deps(api => api.CreateCheckpointAsync("Permit", externalCondition: false, cancellationToken: Ct));
		Assert.False(checkpoint.Unlocked);
		Assert.Empty(await StoredAsync(checkpoint.Id));

		await Deps(api => api.SetExternalConditionAsync(checkpoint.Id, true, Ct));
		Assert.True((await Deps(api => api.GetCheckpointAsync(checkpoint.Id, Ct)))!.Unlocked);
		Record(await StoredAsync(checkpoint.Id), LifecycleState.Finished);

		await Deps(api => api.SetExternalConditionAsync(checkpoint.Id, false, Ct));
		Assert.Empty(await StoredAsync(checkpoint.Id));
	}

	[Fact]
	public void Each_kind_declares_the_states_it_passes_through()
	{
		var resolver = Vault.GetSingleton<EntityLifecycleResolver>();
		LifecycleState[] all = [LifecycleState.Deferred, LifecycleState.Open, LifecycleState.Started, LifecycleState.Finished];

		Assert.Equal(all, resolver.StatesOf(typeof(Objective)).Order());
		Assert.Equal(all, resolver.StatesOf(typeof(StellarDirective)).Order());
		Assert.Equal(all, resolver.StatesOf(typeof(Eventive)).Order());
		// A fate finishes by passing (PEP103 I-191).
		Assert.Equal([LifecycleState.Deferred, LifecycleState.Started, LifecycleState.Finished], resolver.StatesOf(typeof(Fate)).Order());
		Assert.Equal([LifecycleState.Deferred, LifecycleState.Started], resolver.StatesOf(typeof(Decree)).Order());
		Assert.Equal([LifecycleState.Deferred, LifecycleState.Started, LifecycleState.Finished], resolver.StatesOf(typeof(LunarDirective)).Order());
		Assert.Equal([LifecycleState.Deferred, LifecycleState.Open, LifecycleState.Finished], resolver.StatesOf(typeof(Checkpoint)).Order());
		Assert.Equal([LifecycleState.Deferred, LifecycleState.Open, LifecycleState.Finished], resolver.StatesOf(typeof(Attentive)).Order());
		Assert.False(resolver.IsLifecycleKind(typeof(PolarisCycle)));

		// A checkpoint finishes by unlocking, and is deferred once its due passes while it is still locked.
		var locked = new Checkpoint { Id = "c0001", Title = "Locked" };
		var unlocked = new Checkpoint { Id = "c0002", Title = "Unlocked", Unlocked = true };
		var overdue = new Checkpoint { Id = "c0003", Title = "Overdue", Due = new Due { Moment = DateTime.Now.AddDays(-1) } };
		Assert.Equal(LifecycleState.Open, resolver.StateOf(locked));
		Assert.Equal(LifecycleState.Finished, resolver.StateOf(unlocked));
		Assert.Equal(LifecycleState.Deferred, resolver.StateOf(overdue));
	}

	[Fact]
	public void The_default_status_of_a_state_is_its_lowest_valued_member()
	{
		var resolver = Vault.GetSingleton<EntityLifecycleResolver>();

		Assert.Equal(ObjectiveStatus.Failed, resolver.DefaultStatus(typeof(Objective), LifecycleState.Deferred));
		Assert.Equal(ObjectiveStatus.Standby, resolver.DefaultStatus(typeof(Objective), LifecycleState.Open));
		Assert.Equal(ObjectiveStatus.Polaris, resolver.DefaultStatus(typeof(Objective), LifecycleState.Started));
		Assert.Equal(ObjectiveStatus.Done, resolver.DefaultStatus(typeof(Objective), LifecycleState.Finished));
		Assert.Equal(DirectiveStatus.Planned, resolver.DefaultStatus(typeof(StellarDirective), LifecycleState.Open));
		Assert.Equal(DirectiveStatus.Fulfilled, resolver.DefaultStatus(typeof(StellarDirective), LifecycleState.Finished));
		Assert.Equal(FateStatus.Stopped, resolver.DefaultStatus(typeof(Fate), LifecycleState.Deferred));
		Assert.Equal(FateStatus.Passed, resolver.DefaultStatus(typeof(Fate), LifecycleState.Finished));
		Assert.Equal(LunarDirectiveStatus.Stale, resolver.DefaultStatus(typeof(LunarDirective), LifecycleState.Finished));
		Assert.Equal(LunarDirectiveStatus.OnHold, resolver.DefaultStatus(typeof(LunarDirective), LifecycleState.Deferred));
		Assert.Equal(EventiveResolution.Missed, resolver.DefaultStatus(typeof(Eventive), LifecycleState.Deferred));

		// A state a kind never passes through, or reaches without a status of its own, has no default.
		Assert.Null(resolver.DefaultStatus(typeof(Fate), LifecycleState.Open));
		Assert.Null(resolver.DefaultStatus(typeof(Eventive), LifecycleState.Started));
		Assert.Null(resolver.DefaultStatus(typeof(Checkpoint), LifecycleState.Finished));
		Assert.Equal(AttentiveResolution.Done, LifecycleDeclarations.DefaultStatus<AttentiveResolution>(LifecycleState.Finished));
	}

	[Fact]
	public void Next_states_and_transitions_are_read_one_way()
	{
		var resolver = Vault.GetSingleton<EntityLifecycleResolver>();

		// The next state up the order, and the next one a dependency can hold back.
		Assert.Equal(LifecycleState.Open, resolver.NextState(typeof(Objective), LifecycleState.Deferred));
		Assert.Equal(LifecycleState.Started, resolver.NextGatedState(typeof(Objective), LifecycleState.Deferred));
		Assert.Equal(LifecycleState.Finished, resolver.NextGatedState(typeof(Objective), LifecycleState.Started));
		Assert.Null(resolver.NextState(typeof(Objective), LifecycleState.Finished));
		Assert.Equal(LifecycleState.Started, resolver.NextState(typeof(Fate), LifecycleState.Deferred));
		Assert.Equal(LifecycleState.Finished, resolver.NextGatedState(typeof(Fate), LifecycleState.Started));
		Assert.Null(resolver.NextGatedState(typeof(Decree), LifecycleState.Started));
		Assert.Equal(LifecycleState.Finished, resolver.NextGatedState(typeof(Checkpoint), LifecycleState.Deferred));

		// A jump enters every progress state on the way; a move back leaves them, latest first; a shift within one state,
		// a deferral or a resume's way back is no move along the main line.
		Assert.Equal([LifecycleState.Started, LifecycleState.Finished], resolver.Transition(ObjectiveStatus.Standby, ObjectiveStatus.Done).Entered);
		Assert.Equal([LifecycleState.Started, LifecycleState.Finished], resolver.Transition(ObjectiveStatus.Failed, ObjectiveStatus.Archived).Entered);
		Assert.Equal([LifecycleState.Finished, LifecycleState.Started], resolver.Transition(ObjectiveStatus.Done, ObjectiveStatus.Blocked).Left);
		Assert.False(resolver.Transition(ObjectiveStatus.Standby, ObjectiveStatus.Onboard).IsChange);
		var deferral = resolver.Transition(ObjectiveStatus.Polaris, ObjectiveStatus.Failed);
		Assert.True(deferral.Defers);
		Assert.Empty(deferral.Entered);
		Assert.Empty(deferral.Left);
		Assert.Equal([LifecycleState.Finished], resolver.Transition(typeof(Checkpoint), LifecycleState.Open, LifecycleState.Finished).Entered);
	}

	// --- plans and replacement ---

	[Fact]
	public async Task Adding_a_plan_to_an_occupied_slot_replaces_it()
	{
		var objective = await Objective(api => api.CreateStandaloneAsync("Plotted", cancellationToken: Ct));
		var first = await Schedules(api => api.AddAsync(objective.Id, BacklogScheduleType.Plan, LifecycleState.Started, new DateTime(2026, 10, 5), Ct));
		var second = await Schedules(api => api.AddAsync(objective.Id, BacklogScheduleType.Plan, LifecycleState.Started, new DateTime(2026, 10, 7, 9, 30, 0), Ct));
		var finish = await Schedules(api => api.AddAsync(objective.Id, BacklogScheduleType.Plan, LifecycleState.Finished, new DateTime(2026, 10, 20), Ct));

		var schedules = await StoredAsync(objective.Id);
		var begin = Assert.Single(schedules, schedule => schedule.Type == BacklogScheduleType.Plan && schedule.State == LifecycleState.Started);
		Assert.Equal(second.Id, begin.Id);
		Assert.NotEqual(first.Id, begin.Id);
		Assert.Equal(new DateTime(2026, 10, 7, 9, 30, 0), begin.Time);
		Assert.Equal(finish.Id, Slot(schedules, BacklogScheduleType.Plan, LifecycleState.Finished)!.Id);

		// A record is its own slot: beginning the objective keeps the plan beside it.
		await ShiftAsync(objective.Id, ObjectiveStatus.Polaris);
		var withRecord = await StoredAsync(objective.Id);
		Assert.Equal(3, withRecord.Count);
		Assert.Equal(second.Id, Slot(withRecord, BacklogScheduleType.Plan, LifecycleState.Started)!.Id);
		Record(withRecord, LifecycleState.Started);
	}

	[Fact]
	public async Task Directives_and_checkpoints_hold_plans_too()
	{
		var directive = await Directive(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Ct));
		var lunar = await Directive(api => api.CreateLunarAsync("Moon law", cancellationToken: Ct));
		var checkpoint = await Deps(api => api.CreateCheckpointAsync("Gate", cancellationToken: Ct));

		await Schedules(api => api.AddAsync(directive.Id, BacklogScheduleType.Plan, LifecycleState.Finished, new DateTime(2026, 12, 1), Ct));
		await Schedules(api => api.AddAsync(directive.Id, BacklogScheduleType.Plan, LifecycleState.Finished, new DateTime(2026, 12, 15), Ct));
		await Schedules(api => api.AddAsync(lunar.Id, BacklogScheduleType.Plan, LifecycleState.Started, new DateTime(2026, 11, 1), Ct));
		await Schedules(api => api.AddAsync(checkpoint.Id, BacklogScheduleType.Plan, LifecycleState.Finished, new DateTime(2026, 11, 30), Ct));
		await Schedules(api => api.AddAsync(checkpoint.Id, BacklogScheduleType.Plan, LifecycleState.Finished, new DateTime(2026, 11, 29), Ct));

		Assert.Equal(new DateTime(2026, 12, 15), Assert.Single(await StoredAsync(directive.Id)).Time);
		// A lunar directive starts, so it takes a start plan, beside the Started record it was created with (Active).
		Assert.Equal(lunar.Id, Slot(await StoredAsync(lunar.Id), BacklogScheduleType.Plan, LifecycleState.Started)!.DirectiveId);
		var checkpointPlan = Slot(await StoredAsync(checkpoint.Id), BacklogScheduleType.Plan, LifecycleState.Finished)!;
		Assert.Equal(checkpoint.Id, checkpointPlan.CheckpointId);
		Assert.Equal(new DateTime(2026, 11, 29), checkpointPlan.Time);

		// A checkpoint never starts, so there is no start of it to plan; a decree never finishes, while a lunar directive does
		// (Stale, PEP103 patch 6) and a fate does by passing (PEP103 I-191), so their finishes can be planned.
		await Assert.ThrowsAsync<InvalidOperationException>(() => Schedules(api => api.AddAsync(checkpoint.Id, BacklogScheduleType.Plan, LifecycleState.Started, new DateTime(2026, 11, 1), Ct)));
		Assert.Null(Slot(await StoredAsync(checkpoint.Id), BacklogScheduleType.Plan, LifecycleState.Started));
		var decree = await Declarative(api => api.CreateDecreeAsync(new DecreePlan("Tidy up", DefaultLength: 10), Ct));
		await Assert.ThrowsAsync<InvalidOperationException>(() => Schedules(api => api.AddAsync(decree.Id, BacklogScheduleType.Plan, LifecycleState.Finished, new DateTime(2026, 11, 1), Ct)));
		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Solstice", Date: new DateOnly(2027, 12, 21)), Ct));
		await Schedules(api => api.AddAsync(fate.Id, BacklogScheduleType.Plan, LifecycleState.Finished, new DateTime(2027, 12, 22), Ct));
		Assert.Equal(fate.Id, Slot(await StoredAsync(fate.Id), BacklogScheduleType.Plan, LifecycleState.Finished)!.IncentiveId);
		await Schedules(api => api.AddAsync(lunar.Id, BacklogScheduleType.Plan, LifecycleState.Finished, new DateTime(2026, 11, 1), Ct));
	}

	[Fact]
	public async Task Replacement_holds_for_a_bare_context_save_and_the_later_of_two_additions_wins()
	{
		var objective = await Objective(api => api.CreateStandaloneAsync("Bare", cancellationToken: Ct));

		// A save that bypasses the API gets the same rule, since it rides the save hook.
		await Vault.WithScopeAsync(async services =>
		{
			var context = services.GetRequiredService<PlainfraContext>();
			context.BacklogSchedules.Add(new BacklogSchedule { IncentiveId = objective.Id, Type = BacklogScheduleType.Plan, State = LifecycleState.Started, Time = new DateTime(2026, 10, 1) });
			await context.SaveChangesAsync(Ct);
		});
		await Vault.WithScopeAsync(async services =>
		{
			var context = services.GetRequiredService<PlainfraContext>();
			context.BacklogSchedules.Add(new BacklogSchedule { IncentiveId = objective.Id, Type = BacklogScheduleType.Plan, State = LifecycleState.Started, Time = new DateTime(2026, 10, 2) });
			await context.SaveChangesAsync(Ct);
		});
		Assert.Equal(new DateTime(2026, 10, 2), Assert.Single(await StoredAsync(objective.Id)).Time);

		// Two additions to one slot in a single save keep the later one.
		await Vault.WithScopeAsync(async services =>
		{
			var context = services.GetRequiredService<PlainfraContext>();
			context.BacklogSchedules.Add(new BacklogSchedule { IncentiveId = objective.Id, Type = BacklogScheduleType.Plan, State = LifecycleState.Started, Time = new DateTime(2026, 10, 3) });
			context.BacklogSchedules.Add(new BacklogSchedule { IncentiveId = objective.Id, Type = BacklogScheduleType.Plan, State = LifecycleState.Started, Time = new DateTime(2026, 10, 4) });
			await context.SaveChangesAsync(Ct);
		});
		Assert.Equal(new DateTime(2026, 10, 4), Assert.Single(await StoredAsync(objective.Id)).Time);
	}

	[Fact]
	public async Task A_schedule_names_exactly_one_owner()
	{
		var objective = await Objective(api => api.CreateStandaloneAsync("Owner", cancellationToken: Ct));
		var directive = await Directive(api => api.CreateStandaloneAsync("Other owner", cancellationToken: Ct));

		async Task SaveAsync(BacklogSchedule schedule) => await Vault.WithScopeAsync(async services =>
		{
			var context = services.GetRequiredService<PlainfraContext>();
			context.BacklogSchedules.Add(schedule);
			await context.SaveChangesAsync(Ct);
		});

		await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(new BacklogSchedule { Type = BacklogScheduleType.Plan, State = LifecycleState.Started, Time = new DateTime(2026, 10, 1) }));
		await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(new BacklogSchedule { IncentiveId = objective.Id, DirectiveId = directive.Id, Type = BacklogScheduleType.Plan, State = LifecycleState.Started, Time = new DateTime(2026, 10, 1) }));
		Assert.Throws<ArgumentException>(() => BacklogSchedule.For(new PolarisCycle { Id = "x", Title = "x" }, BacklogScheduleType.Plan, LifecycleState.Started, DateTime.Today));
	}

	// --- the API surface ---

	[Fact]
	public async Task Only_plans_are_added_or_deleted_by_hand()
	{
		var objective = await Objective(api => api.CreateStandaloneAsync("Hands off", cancellationToken: Ct));
		await ShiftAsync(objective.Id, ObjectiveStatus.Polaris);
		var record = Record(await StoredAsync(objective.Id), LifecycleState.Started);

		await Assert.ThrowsAsync<InvalidOperationException>(() => Schedules(api => api.AddAsync(objective.Id, BacklogScheduleType.Record, LifecycleState.Finished, DateTime.Today, Ct)));
		await Assert.ThrowsAsync<InvalidOperationException>(() => Schedules(api => api.DeleteAsync(record.Id, Ct)));
		await Assert.ThrowsAsync<InvalidOperationException>(() => Schedules(api => api.AddAsync("j99999999", BacklogScheduleType.Plan, LifecycleState.Started, DateTime.Today, Ct)));
		await Assert.ThrowsAsync<InvalidOperationException>(() => Schedules(api => api.AddAsync(objective.Id, (BacklogScheduleType)42, LifecycleState.Started, DateTime.Today, Ct)));
		await Assert.ThrowsAsync<InvalidOperationException>(() => Schedules(api => api.AddAsync(objective.Id, BacklogScheduleType.Plan, (LifecycleState)42, DateTime.Today, Ct)));
		// Plans place a start or a finish: neither Open, where every item stands before it starts, nor a deferral is planned,
		// even for a kind that passes through it.
		var lunar = await Directive(api => api.CreateLunarAsync("Moon law", cancellationToken: Ct));
		await Assert.ThrowsAsync<InvalidOperationException>(() => Schedules(api => api.AddAsync(objective.Id, BacklogScheduleType.Plan, LifecycleState.Open, DateTime.Today, Ct)));
		await Assert.ThrowsAsync<InvalidOperationException>(() => Schedules(api => api.AddAsync(objective.Id, BacklogScheduleType.Plan, LifecycleState.Deferred, DateTime.Today, Ct)));
		await Assert.ThrowsAsync<InvalidOperationException>(() => Schedules(api => api.AddAsync(lunar.Id, BacklogScheduleType.Plan, LifecycleState.Deferred, DateTime.Today, Ct)));
		await Assert.ThrowsAsync<InvalidOperationException>(() => Schedules(api => api.DeleteAsync(987654, Ct)));

		var plan = await Schedules(api => api.AddAsync(objective.Id, BacklogScheduleType.Plan, LifecycleState.Finished, new DateTime(2026, 10, 31), Ct));
		await Schedules(api => api.DeleteAsync(plan.Id, Ct));
		Assert.Equal(record.Id, Assert.Single(await StoredAsync(objective.Id)).Id);
	}

	[Fact]
	public async Task Listing_filters_by_owner_and_orders_by_time()
	{
		var objective = await Objective(api => api.CreateStandaloneAsync("Listed", cancellationToken: Ct));
		var directive = await Directive(api => api.CreateStandaloneAsync("Also listed", cancellationToken: Ct));
		await Schedules(api => api.AddAsync(objective.Id, BacklogScheduleType.Plan, LifecycleState.Finished, new DateTime(2026, 11, 1), Ct));
		await Schedules(api => api.AddAsync(objective.Id, BacklogScheduleType.Plan, LifecycleState.Started, new DateTime(2026, 10, 1), Ct));
		await Schedules(api => api.AddAsync(directive.Id, BacklogScheduleType.Plan, LifecycleState.Started, new DateTime(2026, 9, 1), Ct));

		var owned = await Schedules(api => api.ListAsync(objective.Id, Ct));
		Assert.Equal([LifecycleState.Started, LifecycleState.Finished], owned.Select(schedule => schedule.State));
		Assert.All(owned, schedule => Assert.Equal(objective.Id, schedule.OwnerId));

		var all = await Schedules(api => api.ListAsync(cancellationToken: Ct));
		Assert.Equal([directive.Id, objective.Id, objective.Id], all.Select(schedule => schedule.OwnerId));
	}

	[Fact]
	public async Task A_time_sent_as_an_instant_is_kept_as_the_local_wall_clock()
	{
		var objective = await Objective(api => api.CreateStandaloneAsync("Timed", cancellationToken: Ct));
		var instant = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

		var plan = await Schedules(api => api.AddAsync(objective.Id, BacklogScheduleType.Plan, LifecycleState.Started, instant, Ct));

		var expected = DateTime.SpecifyKind(instant.ToLocalTime(), DateTimeKind.Unspecified);
		Assert.Equal(expected, plan.Time);
		Assert.Equal(DateTimeKind.Unspecified, plan.Time.Kind);
		Assert.Equal(expected, Assert.Single(await StoredAsync(objective.Id)).Time);
	}

	[Fact]
	public async Task Schedules_go_with_their_owner()
	{
		var objective = await Objective(api => api.CreateStandaloneAsync("Doomed objective", cancellationToken: Ct));
		var directive = await Directive(api => api.CreateStandaloneAsync("Doomed directive", cancellationToken: Ct));
		var checkpoint = await Deps(api => api.CreateCheckpointAsync("Doomed gate", cancellationToken: Ct));
		await ShiftAsync(objective.Id, ObjectiveStatus.Done);
		await Schedules(api => api.AddAsync(objective.Id, BacklogScheduleType.Plan, LifecycleState.Finished, new DateTime(2026, 10, 1), Ct));
		await Schedules(api => api.AddAsync(directive.Id, BacklogScheduleType.Plan, LifecycleState.Started, new DateTime(2026, 10, 1), Ct));
		await Schedules(api => api.AddAsync(checkpoint.Id, BacklogScheduleType.Plan, LifecycleState.Finished, new DateTime(2026, 10, 1), Ct));
		Assert.Equal(3, (await StoredAsync(objective.Id)).Count);

		// A cascading key never refuses a delete, and takes the schedules along.
		await Objective(api => api.DeleteAsync(objective.Id, Ct));
		await Directive(api => api.DeleteAsync(directive.Id, Ct));
		await Deps(api => api.DeleteCheckpointAsync(checkpoint.Id, Ct));

		Assert.Equal(0, await Vault.QueryAsync(context => context.BacklogSchedules.CountAsync(Ct)));
	}

	[Fact]
	public async Task A_recorded_schedule_is_announced_as_a_change_to_its_owner()
	{
		var objective = await Objective(api => api.CreateStandaloneAsync("Announced", cancellationToken: Ct));
		var broker = Vault.GetSingleton<PlaintorchChangeBroker>();
		using var subscription = broker.Subscribe();

		await Schedules(api => api.AddAsync(objective.Id, BacklogScheduleType.Plan, LifecycleState.Started, new DateTime(2026, 10, 1), Ct));

		var changes = new List<EntityChange>();
		while (subscription.Reader.TryRead(out var change))
		{
			changes.Add(change);
		}

		// A schedule has no identity a client tracks, so the feed names the owner it hangs off (through the one foreign key
		// that is set), which revalidates whatever the client shows of it. The key only knows the incentive family; the
		// owner, read untracked by the API, is still announced under the concrete type the API serializes it as.
		Assert.Contains(changes, change => change.Type == nameof(Objective) && change.Id == objective.Id && change.Operation == EntityChangeOperation.Modified);
		Assert.DoesNotContain(changes, change => change.Type == nameof(Incentive));
	}

	[Fact]
	public async Task Recording_a_directive_announces_the_directive_by_its_own_kind()
	{
		var directive = await Directive(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Ct));
		var broker = Vault.GetSingleton<PlaintorchChangeBroker>();
		using var subscription = broker.Subscribe();

		await Directive(api => api.ShiftStellarWorkflowAsync(directive.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Active), Ct));

		var changes = new List<EntityChange>();
		while (subscription.Reader.TryRead(out var change))
		{
			changes.Add(change);
		}

		Assert.Contains(changes, change => change.Type == nameof(StellarDirective) && change.Id == directive.Id);
		Assert.DoesNotContain(changes, change => change.Type == nameof(Pleiades.Orchestration.Directive));
	}

	// --- the wire ---

	[Fact]
	public void The_wire_shape_carries_numeric_type_and_state_and_a_civil_time()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		PlaintorchHostFactory.ConfigureSerializer(options);
		var schedule = BacklogSchedule.For(
			new StellarDirective { Id = "A12345678", Title = "Campaign" },
			BacklogScheduleType.Record,
			LifecycleState.Finished,
			new DateTime(2026, 10, 5, 9, 30, 0));
		schedule.Id = 12;

		var json = Assert.IsType<JsonObject>(JsonNode.Parse(JsonSerializer.Serialize(schedule, options)));

		Assert.Equal("BacklogSchedule", json["@type"]!.GetValue<string>());
		Assert.Equal(12, json["id"]!.GetValue<long>());
		Assert.Equal(1, json["type"]!.GetValue<int>());
		Assert.Equal(2, json["state"]!.GetValue<int>());
		Assert.Equal("2026-10-05T09:30:00", json["time"]!.GetValue<string>());
		Assert.Equal("A12345678", json["directiveId"]!.GetValue<string>());
		Assert.Equal("A12345678", json["ownerId"]!.GetValue<string>());
		Assert.Null(json["incentiveId"]);
		Assert.Null(json["checkpointId"]);
	}
}
