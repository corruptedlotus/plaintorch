using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orbits;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Plaintorch.Api;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Enforcement;
using Pleiades.Plaintorch.Materialization;
using Pleiades.Plaintorch.Preferences;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Fates pass (PEP103 I-191, user ruling): "Passed" is inferred and selected. A running fate whose eventives have all fully
/// passed switches to it, on the save that makes it so or on the rolling pass once time does, wherever nothing blocks it; a
/// manual occurrence still to come, or an orbit change that begets one, switches it back. Selected by hand it acts like
/// Stopped, except that a manual occurrence still to come still brings it back. Passed is a finish: an edge waiting on a
/// fate's finish holds while it is Passed, and its history records the pass and the way back.
/// </summary>
public sealed class FatePassedTests : VaultTestBase
{
	private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

	private CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> Declarative<T>(Func<IDeclarativeApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDeclarativeApi>()));
	private Task<T> Objective<T>(Func<IObjectiveApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IObjectiveApi>()));
	private Task<T> Deps<T>(Func<IDependencyApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDependencyApi>()));
	private Task<int> HardenAt(DateTimeOffset now) => Vault.WithScopeAsync(services => services.GetRequiredService<ProximityMaterializationService>().MaterializeForNowAsync(now, Ct));

	/// <summary>Runs as an API request would: someone can be asked.</summary>
	private Task<T> Interactive<T>(Func<IServiceProvider, Task<T>> action) => Vault.WithScopeAsync(services =>
	{
		services.GetRequiredService<PlaintorchRequestFlags>().Interactive = true;
		return action(services);
	});

	private Task SetEnforcementAsync(BacklogStateEnforcementLevel level)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<UserPreferenceService>().SetAsync(PreferenceKeys.BacklogStateEnforcement, level, Ct));

	private Task<Fate> CreateAsync(string title, DateOnly? date = null, string? orbit = null)
		=> Declarative(api => api.CreateFateAsync(new FatePlan(title, Date: date, Orbit: orbit), Ct));

	/// <summary>Creates a fate and materializes its note (a fate's note starts its boundary on request).</summary>
	private Task<Fate> CreateNotedAsync(string title, DateOnly? date = null, string? orbit = null) => Vault.WithScopeAsync(async services =>
	{
		var api = services.GetRequiredService<IDeclarativeApi>();
		var fate = await api.CreateFateAsync(new FatePlan(title, Date: date, Orbit: orbit), Ct);
		return await api.BeginFateBoundaryAsync(fate.Id, Ct);
	});

	private Task<Fate> UpdateAsync(string id, FateUpdate update) => Declarative(api => api.UpdateFateAsync(id, update, Ct));

	private Task<Eventive> AddManualAsync(string fateId, DateOnly day)
		=> Declarative(api => api.CreateEventiveAsync(new EventivePlan(fateId, new Epoch { Moment = day.ToDateTime(TimeOnly.MinValue), Granularity = OrbitUnit.Day }), Ct));

	private Task<Fate> FateAsync(string id)
		=> Vault.QueryAsync(context => context.Fates.AsNoTracking().IgnoreAutoIncludes().SingleAsync(fate => fate.Id == id, Ct));

	private Task<int> EventiveCountAsync(string fateId)
		=> Vault.QueryAsync(context => context.Eventives.CountAsync(eventive => eventive.FateId == fateId, Ct));

	private Task<ObjectiveStatus> ObjectiveStatusAsync(string id)
		=> Vault.QueryAsync(context => context.Objectives.AsNoTracking().IgnoreAutoIncludes()
			.Where(objective => objective.Id == id).Select(objective => objective.Status).SingleAsync(Ct));

	/// <summary>The records an owner holds, oldest first.</summary>
	private Task<List<LifecycleState>> RecordsAsync(string ownerId)
		=> Vault.QueryAsync(context => context.BacklogSchedules.AsNoTracking()
			.Where(schedule => schedule.Type == BacklogScheduleType.Record && schedule.IncentiveId == ownerId)
			.OrderBy(schedule => schedule.Id)
			.Select(schedule => schedule.State)
			.ToListAsync(Ct));

	private Task<bool> SatisfiedAsync(long edgeId)
		=> Vault.QueryAsync(context => context.Dependencies.AsNoTracking().Where(edge => edge.Id == edgeId).Select(edge => edge.Satisfied).SingleAsync(Ct));

	private static EndpointRef FateRef(string id) => new(DependencyEndpointKind.Fate, id);

	private static EndpointRef ObjectiveRef(string id) => new(DependencyEndpointKind.Objective, id);

	private static DateTimeOffset At(DateOnly day, int hour) => new(day.ToDateTime(new TimeOnly(hour, 0)));

	private static string Note(string title) => $"Fates/{title}.md";

	private static void AssertInferred(Fate fate, FateStatus from)
	{
		Assert.Equal(FateStatus.Passed, fate.Status);
		Assert.True(fate.PassInferred);
		Assert.Equal(from, fate.PassedFrom);
	}

	[Fact]
	public void Passed_is_the_finish_a_fate_reaches()
	{
		Assert.Equal(LifecycleState.Finished, LifecycleDeclarations.StateOf(FateStatus.Passed));
		Assert.False(TemporalActivation.IsActive(FateStatus.Passed));
		Assert.Equal(FateStatus.Passed, LifecycleDeclarations.DefaultStatus<FateStatus>(LifecycleState.Finished));
	}

	[Fact]
	public async Task A_fate_whose_eventives_have_all_passed_passes_on_the_save_and_keeps_its_occurrence()
	{
		var day = Today.AddDays(-3);
		var fate = await CreateAsync("Dentist", day);

		AssertInferred(await FateAsync(fate.Id), FateStatus.Active);
		// What time brought hardens before the fate passes, so its occurrence stays once its projection stops.
		var eventive = Assert.Single(await Declarative(api => api.ListEventivesAsync(fate.Id, Ct)));
		Assert.Equal(day.ToDateTime(TimeOnly.MinValue), eventive.RecurrenceId);
		Assert.Null((await FateAsync(fate.Id)).NextOccurrence);
	}

	[Fact]
	public async Task A_fate_passes_by_time_alone_on_the_rolling_pass_and_records_it()
	{
		var day = Today.AddDays(1);
		var fate = await CreateNotedAsync("Concert", day);
		Assert.Equal(FateStatus.Active, (await FateAsync(fate.Id)).Status);

		await HardenAt(At(day.AddDays(2), 12));

		AssertInferred(await FateAsync(fate.Id), FateStatus.Active);
		Assert.Equal([LifecycleState.Started, LifecycleState.Finished], await RecordsAsync(fate.Id));
		Assert.Contains("status: Passed", Vault.ReadVaultFile(Note("Concert")));
	}

	[Fact]
	public async Task Fates_whose_orbits_ran_out_before_the_upgrade_pass_together_on_the_first_rolling_pass()
	{
		var titles = new[] { "Launch", "Recital", "Summit" };
		var ran = new List<Fate>();
		foreach (var (title, index) in titles.Select((title, index) => (title, index)))
		{
			ran.Add(await CreateNotedAsync(title, Today.AddDays(-5 - index)));
		}

		var running = await CreateAsync("Standup", orbit: "d");
		var ids = ran.Select(fate => fate.Id).ToList();

		// As a core before Passed left them: running, their occurrences hardened, their notes reading Active, no pass recorded.
		await Vault.QueryAsync(async context =>
		{
			await context.Fates.Where(fate => ids.Contains(fate.Id)).ExecuteUpdateAsync(setters => setters
				.SetProperty(fate => fate.Status, FateStatus.Active)
				.SetProperty(fate => fate.PassInferred, false)
				.SetProperty(fate => fate.PassedFrom, (FateStatus?)null), Ct);
			return await context.BacklogSchedules
				.Where(schedule => schedule.IncentiveId != null && ids.Contains(schedule.IncentiveId) && schedule.State == LifecycleState.Finished)
				.ExecuteDeleteAsync(Ct);
		});
		foreach (var title in titles)
		{
			Vault.WriteVaultFile(Note(title), Vault.ReadVaultFile(Note(title)).Replace("status: Passed", "status: Active", StringComparison.Ordinal));
		}

		await HardenAt(DateTimeOffset.Now);

		foreach (var (fate, title) in ran.Zip(titles))
		{
			AssertInferred(await FateAsync(fate.Id), FateStatus.Active);
			Assert.Equal([LifecycleState.Started, LifecycleState.Finished], await RecordsAsync(fate.Id));
			Assert.Contains("status: Passed", Vault.ReadVaultFile(Note(title)));
		}

		Assert.Equal(FateStatus.Active, (await FateAsync(running.Id)).Status);

		// A second pass finds nothing more to do.
		await HardenAt(DateTimeOffset.Now);
		Assert.All(await Task.WhenAll(ids.Select(RecordsAsync)), records => Assert.Equal(2, records.Count));
	}

	[Fact]
	public async Task A_fate_passes_only_where_nothing_blocks_it_and_the_engine_never_compensates_for_its_own_move()
	{
		await SetEnforcementAsync(BacklogStateEnforcementLevel.Auto);
		var fate = await CreateAsync("Graduation", Today.AddDays(10));
		var thesis = await Objective(api => api.CreateStandaloneAsync("Thesis", cancellationToken: Ct));
		await Deps(api => api.CreateAsync(ObjectiveRef(thesis.Id), FateRef(fate.Id), constraint: DependencyConstraint.ToFinish, cancellationToken: Ct));

		// Its orbit runs out, but the thesis must finish first: it keeps running, and nothing is moved to let it pass.
		var past = Today.AddDays(-2);
		await UpdateAsync(fate.Id, new FateUpdate(Orbit: $"Z{{{past.Year}/{past.Month}/{past.Day}}}"));
		Assert.Equal(FateStatus.Active, (await FateAsync(fate.Id)).Status);
		Assert.Equal(ObjectiveStatus.Standby, await ObjectiveStatusAsync(thesis.Id));

		// Once the thesis is done, the rolling pass lets it pass.
		await Objective(api => api.ShiftWorkflowAsync(thesis.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Done), Ct));
		Assert.Equal(FateStatus.Active, (await FateAsync(fate.Id)).Status);
		await HardenAt(DateTimeOffset.Now);
		AssertInferred(await FateAsync(fate.Id), FateStatus.Active);
	}

	[Fact]
	public async Task A_stopped_fate_and_one_with_nothing_scheduled_never_pass()
	{
		var stopped = await CreateAsync("Festival", Today.AddDays(10));
		await UpdateAsync(stopped.Id, new FateUpdate(Status: FateStatus.Stopped));
		var past = Today.AddDays(-2);
		await UpdateAsync(stopped.Id, new FateUpdate(Orbit: $"Z{{{past.Year}/{past.Month}/{past.Day}}}"));
		var unscheduled = await CreateAsync("Someday");

		await HardenAt(DateTimeOffset.Now);

		Assert.Equal(FateStatus.Stopped, (await FateAsync(stopped.Id)).Status);
		Assert.Equal(FateStatus.Active, (await FateAsync(unscheduled.Id)).Status);
		Assert.Equal(0, await EventiveCountAsync(unscheduled.Id));
	}

	[Fact]
	public async Task A_fate_of_manual_occurrences_alone_passes_once_they_have_closed_and_comes_back_for_one_still_to_come()
	{
		var fate = await CreateAsync("Visits");
		await AddManualAsync(fate.Id, Today.AddDays(-4));
		AssertInferred(await FateAsync(fate.Id), FateStatus.Active);

		// An inferred pass takes one wholly past and stays Passed.
		await AddManualAsync(fate.Id, Today.AddDays(-2));
		AssertInferred(await FateAsync(fate.Id), FateStatus.Active);

		await AddManualAsync(fate.Id, Today.AddDays(3));
		var back = await FateAsync(fate.Id);
		Assert.Equal(FateStatus.Active, back.Status);
		Assert.False(back.PassInferred);
		Assert.Null(back.PassedFrom);
		Assert.Equal(3, await EventiveCountAsync(fate.Id));
	}

	[Fact]
	public async Task Coming_back_restores_the_started_status_the_fate_passed_from_and_records_it()
	{
		var fate = await CreateAsync("Market", Today.AddDays(10));
		await UpdateAsync(fate.Id, new FateUpdate(Status: FateStatus.OptOut));
		var past = Today.AddDays(-2);
		await UpdateAsync(fate.Id, new FateUpdate(Orbit: $"Z{{{past.Year}/{past.Month}/{past.Day}}}"));
		AssertInferred(await FateAsync(fate.Id), FateStatus.OptOut);

		await AddManualAsync(fate.Id, Today.AddDays(2));

		Assert.Equal(FateStatus.OptOut, (await FateAsync(fate.Id)).Status);
		// Opting out records nothing (it stays started); passing records the finish, and coming back a start.
		Assert.Equal([LifecycleState.Started, LifecycleState.Finished, LifecycleState.Started], await RecordsAsync(fate.Id));
	}

	[Fact]
	public async Task An_inferred_pass_comes_back_when_its_orbit_or_an_eventive_gives_it_an_occurrence_still_to_come()
	{
		var day = Today.AddDays(-3);
		var fate = await CreateAsync("Eclipse", day);
		AssertInferred(await FateAsync(fate.Id), FateStatus.Active);

		// An orbit that begets nothing still to come leaves it Passed.
		var earlier = Today.AddDays(-6);
		await UpdateAsync(fate.Id, new FateUpdate(Orbit: $"Z{{{earlier.Year}/{earlier.Month}/{earlier.Day}}}"));
		AssertInferred(await FateAsync(fate.Id), FateStatus.Active);

		await UpdateAsync(fate.Id, new FateUpdate(Orbit: "d"));
		var back = await FateAsync(fate.Id);
		Assert.Equal(FateStatus.Active, back.Status);
		Assert.NotNull(back.NextOccurrence);

		// Its hardened occurrence moved ahead brings an inferred pass back too.
		var other = await CreateAsync("Comet", day);
		AssertInferred(await FateAsync(other.Id), FateStatus.Active);
		await Declarative(api => api.UpdateEventiveAsync(new EventiveOccurrenceRef(other.Id, day.ToDateTime(TimeOnly.MinValue)), new EventiveUpdate(Date: Today.AddDays(4)), Ct));
		Assert.Equal(FateStatus.Active, (await FateAsync(other.Id)).Status);
	}

	[Fact]
	public async Task A_selected_pass_acts_like_stopped_but_a_manual_occurrence_still_to_come_brings_it_back()
	{
		var fate = await CreateAsync("Standup", orbit: "d");
		await UpdateAsync(fate.Id, new FateUpdate(Status: FateStatus.Passed));
		var selected = await FateAsync(fate.Id);
		Assert.Equal(FateStatus.Passed, selected.Status);
		Assert.False(selected.PassInferred);
		Assert.Equal(FateStatus.Active, selected.PassedFrom);
		Assert.Null(selected.NextOccurrence);

		// Nothing projects, nothing hardens, and an orbit change leaves it Passed.
		var projection = await Vault.WithScopeAsync(services => services.GetRequiredService<AgendaProjectionService>().ProjectAsync(Today, Today.AddDays(3), Ct));
		Assert.DoesNotContain(projection.Eventives, eventive => eventive.FateId == fate.Id);
		await HardenAt(At(Today.AddDays(2), 23));
		Assert.Equal(0, await EventiveCountAsync(fate.Id));
		await UpdateAsync(fate.Id, new FateUpdate(Orbit: "d%2"));
		Assert.Equal(FateStatus.Passed, (await FateAsync(fate.Id)).Status);

		// A manual occurrence wholly past is refused; one still to come is taken and brings it back.
		await Assert.ThrowsAsync<InvalidOperationException>(() => AddManualAsync(fate.Id, Today.AddDays(-1)));
		Assert.Equal(0, await EventiveCountAsync(fate.Id));
		await AddManualAsync(fate.Id, Today.AddDays(2));
		Assert.Equal(FateStatus.Active, (await FateAsync(fate.Id)).Status);
		Assert.Equal([LifecycleState.Started, LifecycleState.Finished, LifecycleState.Started], await RecordsAsync(fate.Id));
	}

	[Fact]
	public async Task A_fully_passed_fate_moved_back_passes_again_as_an_inferred_pass()
	{
		var fate = await CreateAsync("Lecture", Today.AddDays(-3));
		await UpdateAsync(fate.Id, new FateUpdate(Status: FateStatus.Active));
		AssertInferred(await FateAsync(fate.Id), FateStatus.Active);

		// A selected pass taken back on a fully passed fate becomes an inferred one, which an orbit change can bring back.
		await UpdateAsync(fate.Id, new FateUpdate(Status: FateStatus.Stopped));
		await UpdateAsync(fate.Id, new FateUpdate(Status: FateStatus.Passed));
		var selected = await FateAsync(fate.Id);
		Assert.False(selected.PassInferred);
		Assert.Null(selected.PassedFrom);
		await UpdateAsync(fate.Id, new FateUpdate(Status: FateStatus.OptOut));
		AssertInferred(await FateAsync(fate.Id), FateStatus.OptOut);
	}

	[Fact]
	public async Task Finish_edges_from_a_fate_hold_while_it_is_passed_and_drop_when_it_comes_back()
	{
		var fate = await CreateAsync("Standup", orbit: "d");
		var notes = await Objective(api => api.CreateStandaloneAsync("Minutes", cancellationToken: Ct));
		// The default trigger waits for the fate's finish.
		var edge = await Deps(api => api.CreateAsync(FateRef(fate.Id), ObjectiveRef(notes.Id), cancellationToken: Ct));
		Assert.False(edge.Satisfied);

		await UpdateAsync(fate.Id, new FateUpdate(Status: FateStatus.Passed));
		Assert.True(await SatisfiedAsync(edge.Id));

		await AddManualAsync(fate.Id, Today.AddDays(1));
		Assert.Equal(FateStatus.Active, (await FateAsync(fate.Id)).Status);
		Assert.False(await SatisfiedAsync(edge.Id));
	}

	[Fact]
	public async Task Coming_back_is_judged_as_the_edits_move_back()
	{
		var fate = await CreateAsync("Standup", orbit: "d");
		var notes = await Objective(api => api.CreateStandaloneAsync("Minutes", cancellationToken: Ct));
		await Deps(api => api.CreateAsync(FateRef(fate.Id), ObjectiveRef(notes.Id), cancellationToken: Ct));
		await UpdateAsync(fate.Id, new FateUpdate(Status: FateStatus.Passed));
		await Objective(api => api.ShiftWorkflowAsync(notes.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Polaris), Ct));
		var plan = new EventivePlan(fate.Id, new Epoch { Moment = Today.AddDays(1).ToDateTime(TimeOnly.MinValue), Granularity = OrbitUnit.Day });

		// The minutes started on the fate's finish, so bringing it back reopens that: refused under Block, and the occurrence
		// with it.
		await SetEnforcementAsync(BacklogStateEnforcementLevel.Block);
		var refused = await Assert.ThrowsAsync<StateEnforcementException>(() =>
			Interactive(services => services.GetRequiredService<IDeclarativeApi>().CreateEventiveAsync(plan, Ct)));
		Assert.Equal(StateDecisionOutcome.Refuse, refused.Decision.Outcome);
		Assert.Contains(refused.Decision.Conflicts, conflict => conflict.Rule == StateJudge.ReopenRule);
		Assert.Equal(FateStatus.Passed, (await FateAsync(fate.Id)).Status);
		Assert.Equal(0, await EventiveCountAsync(fate.Id));

		// Under Auto it goes ahead, and the minutes move back with it.
		await SetEnforcementAsync(BacklogStateEnforcementLevel.Auto);
		await Interactive(services => services.GetRequiredService<IDeclarativeApi>().CreateEventiveAsync(plan, Ct));
		Assert.Equal(FateStatus.Active, (await FateAsync(fate.Id)).Status);
		Assert.Equal(ObjectiveStatus.Standby, await ObjectiveStatusAsync(notes.Id));
	}

	[Fact]
	public async Task A_compensation_brings_a_passed_fate_back_to_the_started_status_it_passed_from()
	{
		await SetEnforcementAsync(BacklogStateEnforcementLevel.Auto);
		var fate = await CreateAsync("Standup", orbit: "d");
		var agenda = await Objective(api => api.CreateStandaloneAsync("Agenda", cancellationToken: Ct));
		await Deps(api => api.CreateAsync(ObjectiveRef(agenda.Id), FateRef(fate.Id), constraint: DependencyConstraint.ToFinish, cancellationToken: Ct));
		await Objective(api => api.ShiftWorkflowAsync(agenda.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Done), Ct));
		await UpdateAsync(fate.Id, new FateUpdate(Status: FateStatus.OptOut));
		await UpdateAsync(fate.Id, new FateUpdate(Status: FateStatus.Passed));

		// The fate's pass relied on the agenda's finish; taking that back moves the fate back as it was before it passed.
		await Objective(api => api.ShiftWorkflowAsync(agenda.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Standby), Ct));

		var back = await FateAsync(fate.Id);
		Assert.Equal(FateStatus.OptOut, back.Status);
		Assert.Null(back.PassedFrom);
	}

	[Fact]
	public async Task A_pass_the_engine_takes_back_hardens_none_of_what_the_fate_passed_while_passed()
	{
		var fate = await CreateAsync("Standup", orbit: "d");
		await UpdateAsync(fate.Id, new FateUpdate(Status: FateStatus.Passed));

		// Today's occurrence began while the fate was Passed; coming back takes the schedule up from now, as a resume does.
		await AddManualAsync(fate.Id, Today.AddDays(2));
		await HardenAt(DateTimeOffset.Now);

		Assert.Equal(FateStatus.Active, (await FateAsync(fate.Id)).Status);
		var eventive = Assert.Single(await Declarative(api => api.ListEventivesAsync(fate.Id, Ct)));
		Assert.True(eventive.IsManual);
	}

	[Fact]
	public async Task Selecting_passed_meets_the_finish_gate()
	{
		await SetEnforcementAsync(BacklogStateEnforcementLevel.Block);
		var fate = await CreateAsync("Graduation", Today.AddDays(10));
		var thesis = await Objective(api => api.CreateStandaloneAsync("Thesis", cancellationToken: Ct));
		await Deps(api => api.CreateAsync(ObjectiveRef(thesis.Id), FateRef(fate.Id), constraint: DependencyConstraint.ToFinish, cancellationToken: Ct));

		var refused = await Assert.ThrowsAsync<StateEnforcementException>(() =>
			Interactive(services => services.GetRequiredService<IDeclarativeApi>().UpdateFateAsync(fate.Id, new FateUpdate(Status: FateStatus.Passed), Ct)));
		Assert.Contains(refused.Decision.Conflicts, conflict => conflict.Rule == StateJudge.FinishGateRule);
		Assert.Equal(FateStatus.Active, (await FateAsync(fate.Id)).Status);
	}

	[Fact]
	public async Task A_note_reads_and_writes_passed_and_a_frontmatter_sync_keeps_how_the_fate_passed()
	{
		var inferred = await CreateNotedAsync("Dentist", Today.AddDays(-3));
		Assert.Contains("status: Passed", Vault.ReadVaultFile(Note("Dentist")));

		// An unrelated note edit keeps the inferred pass an inferred one.
		Vault.WriteVaultFile(Note("Dentist"), Vault.ReadVaultFile(Note("Dentist")) + "Bring the forms." + Environment.NewLine);
		await Vault.ReconcileAsync(Vault.AbsolutePath(Note("Dentist")));
		AssertInferred(await FateAsync(inferred.Id), FateStatus.Active);

		// A note that selects Passed itself makes a selected pass.
		var running = await CreateNotedAsync("Standup", orbit: "d");
		Assert.Contains("status: Active", Vault.ReadVaultFile(Note("Standup")));
		Vault.WriteVaultFile(Note("Standup"), Vault.ReadVaultFile(Note("Standup")).Replace("status: Active", "status: Passed", StringComparison.Ordinal));
		await Vault.ReconcileAsync(Vault.AbsolutePath(Note("Standup")));
		var selected = await FateAsync(running.Id);
		Assert.Equal(FateStatus.Passed, selected.Status);
		Assert.False(selected.PassInferred);
		Assert.Equal(FateStatus.Active, selected.PassedFrom);
		Assert.Contains("status: Passed", Vault.ReadVaultFile(Note("Standup")));
	}
}
