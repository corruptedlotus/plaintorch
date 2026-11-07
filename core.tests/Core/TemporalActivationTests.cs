using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Materialization;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Initial states, the lifecycles of declaratives and lunar directives, and the one gate on temporal activation (PEP103
/// patch 6, user rulings). Every kind is created in the status its lifecycle declares initial: declaratives and lunar
/// directives already under way. A fate, a decree and a lunar directive keep a history of the states they enter. An opted-out
/// fate is under way, a stopped one set aside, and a stale lunar directive finished. Whatever is finished or set aside has its
/// temporal activation switched off: no orbit occurrence, no due eventive.
/// </summary>
public sealed class TemporalActivationTests : VaultTestBase
{
	private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

	private CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> Declarative<T>(Func<IDeclarativeApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDeclarativeApi>()));
	private Task<T> Directive<T>(Func<IDirectiveApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDirectiveApi>()));
	private Task<T> Objective<T>(Func<IObjectiveApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IObjectiveApi>()));
	private Task<T> Deps<T>(Func<IDependencyApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDependencyApi>()));
	private Task<AgendaProjection> Project(DateOnly start, DateOnly end) => Vault.WithScopeAsync(services => services.GetRequiredService<AgendaProjectionService>().ProjectAsync(start, end, Ct));
	private Task<int> HardenAt(DateTimeOffset now) => Vault.WithScopeAsync(services => services.GetRequiredService<ProximityMaterializationService>().MaterializeForNowAsync(now, Ct));

	private static DateTimeOffset TodayAt(int hour) => new(Today.ToDateTime(new TimeOnly(hour, 0)));

	/// <summary>The records an owner holds, oldest first.</summary>
	private Task<List<LifecycleState>> RecordsAsync(string ownerId)
		=> Vault.QueryAsync(context => context.BacklogSchedules.AsNoTracking()
			.Where(schedule => schedule.Type == BacklogScheduleType.Record
				&& (schedule.IncentiveId == ownerId || schedule.DirectiveId == ownerId))
			.OrderBy(schedule => schedule.Id)
			.Select(schedule => schedule.State)
			.ToListAsync(Ct));

	[Fact]
	public void Every_kind_is_created_in_the_status_its_lifecycle_declares_initial()
	{
		Assert.Equal(ObjectiveStatus.Standby, LifecycleDeclarations.InitialStatus<ObjectiveStatus>());
		Assert.Equal(DirectiveStatus.Planned, LifecycleDeclarations.InitialStatus<DirectiveStatus>());
		Assert.Equal(FateStatus.Active, LifecycleDeclarations.InitialStatus<FateStatus>());
		Assert.Equal(DecreeStatus.Active, LifecycleDeclarations.InitialStatus<DecreeStatus>());
		Assert.Equal(LunarDirectiveStatus.Active, LifecycleDeclarations.InitialStatus<LunarDirectiveStatus>());

		Assert.Equal(LunarDirectiveStatus.Active, new LunarDirective { Id = "LUNA001", Title = "Moon law" }.Status);
		Assert.Equal(LifecycleState.Finished, LifecycleDeclarations.StateOf(LunarDirectiveStatus.Stale));
		Assert.Equal(LifecycleState.Started, LifecycleDeclarations.StateOf(FateStatus.OptOut));
		Assert.Equal(LifecycleState.Deferred, LifecycleDeclarations.StateOf(FateStatus.Stopped));
		Assert.Equal([FateStatus.Active, FateStatus.OptOut], TemporalActivation.Statuses<FateStatus>());
	}

	[Fact]
	public async Task A_lunar_directive_starts_active_and_keeps_a_history_of_its_states()
	{
		var lunar = await Directive(api => api.CreateLunarAsync("Moon law", cancellationToken: Ct));
		Assert.Equal(LunarDirectiveStatus.Active, lunar.Status);

		foreach (var status in new[] { LunarDirectiveStatus.OnHold, LunarDirectiveStatus.Active, LunarDirectiveStatus.Stale, LunarDirectiveStatus.Active })
		{
			await Directive(api => api.ShiftLunarWorkflowAsync(lunar.Id, new LunarDirectiveWorkflowShift(status), Ct));
		}

		// Every state entered is recorded anew, and nothing is withdrawn.
		Assert.Equal(
			[LifecycleState.Started, LifecycleState.Deferred, LifecycleState.Started, LifecycleState.Finished, LifecycleState.Started],
			await RecordsAsync(lunar.Id));
	}

	[Fact]
	public async Task A_fate_keeps_a_history_and_counts_as_under_way_while_opted_out()
	{
		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Daily standup", Orbit: "d", EventDuration: 30), Ct));
		var objective = await Objective(api => api.CreateStandaloneAsync("Prepare", cancellationToken: Ct));
		var edge = await Deps(api => api.CreateAsync(new EndpointRef(DependencyEndpointKind.Fate, fate.Id), new EndpointRef(DependencyEndpointKind.Objective, objective.Id), DependencyTrigger.OnBegin, cancellationToken: Ct));
		Assert.True(edge.Satisfied);

		// Opting out keeps the fate started: no new record, and what waits on its start still holds.
		await Declarative(api => api.UpdateFateAsync(fate.Id, new FateUpdate(Status: FateStatus.OptOut), Ct));
		Assert.True((await Deps(api => api.ListAsync(fate.Id, Ct))).Single().Satisfied);

		await Declarative(api => api.UpdateFateAsync(fate.Id, new FateUpdate(Status: FateStatus.Stopped), Ct));
		Assert.False((await Deps(api => api.ListAsync(fate.Id, Ct))).Single().Satisfied);
		await Declarative(api => api.UpdateFateAsync(fate.Id, new FateUpdate(Status: FateStatus.Active), Ct));
		await Declarative(api => api.UpdateFateAsync(fate.Id, new FateUpdate(Status: FateStatus.Stopped), Ct));

		Assert.Equal(
			[LifecycleState.Started, LifecycleState.Deferred, LifecycleState.Started, LifecycleState.Deferred],
			await RecordsAsync(fate.Id));
	}

	[Fact]
	public async Task Taking_an_opted_out_fate_back_hardens_none_of_the_occurrences_it_passed()
	{
		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Daily standup", Orbit: "d", EventDuration: 30), Ct));
		await Declarative(api => api.UpdateFateAsync(fate.Id, new FateUpdate(Status: FateStatus.OptOut), Ct));
		Assert.Equal(0, await HardenAt(TodayAt(23)));

		// Its next occurrence still rolls forward while opted out.
		var next = await Vault.QueryAsync(context => context.Fates.AsNoTracking().Where(item => item.Id == fate.Id).Select(item => item.NextOccurrence).SingleAsync(Ct));
		Assert.NotNull(next);
		Assert.True(DateOnly.FromDateTime(next.Value) > Today);

		await Declarative(api => api.UpdateFateAsync(fate.Id, new FateUpdate(Status: FateStatus.Active), Ct));
		Assert.Equal(0, await HardenAt(TodayAt(23)));
		Assert.Equal(0, await Vault.QueryAsync(context => context.Eventives.CountAsync(item => item.FateId == fate.Id, Ct)));
	}

	[Theory]
	[InlineData(ObjectiveStatus.Done)]
	[InlineData(ObjectiveStatus.Archived)]
	[InlineData(ObjectiveStatus.Failed)]
	public async Task A_finished_or_failed_objectives_due_no_longer_acts(ObjectiveStatus status)
	{
		var live = await Objective(api => api.CreateStandaloneAsync("Live work", cancellationToken: Ct));
		var closed = await Objective(api => api.CreateStandaloneAsync("Closed work", cancellationToken: Ct));
		foreach (var objective in new[] { live, closed })
		{
			await Objective(api => api.UpdateAsync(objective.Id, new ObjectiveUpdate(Due: Due.On(Today)), Ct));
		}

		await Objective(api => api.ShiftWorkflowAsync(closed.Id, new ObjectiveWorkflowShift(status), Ct));

		var projected = (await Project(Today, Today.AddDays(1))).Terminatives.Select(terminative => terminative.ObjectiveId).ToList();
		Assert.Contains(live.Id, projected);
		Assert.DoesNotContain(closed.Id, projected);

		await HardenAt(TodayAt(23));
		var hardened = await Vault.QueryAsync(context => context.Terminatives.AsNoTracking().Where(item => item.ObjectiveId != null).Select(item => item.ObjectiveId).ToListAsync(Ct));
		Assert.Equal([live.Id], hardened);
	}

	[Fact]
	public async Task A_stale_lunar_directive_switches_its_timeframes_off_and_on_hold_keeps_them()
	{
		var lunar = await Directive(api => api.CreateLunarAsync("Moon law", cancellationToken: Ct));
		var morning = await Directive(api => api.CreateTimeframeAsync(
			lunar.Id,
			new TimeframePlan("Morning", new TimeOnly(9, 0), new TimeOnly(12, 0), AutoInclusion: TimeframeInclusion.College, AutoInclusionColleges: [ObjectiveCollege.Lore]),
			Ct));
		await Vault.WithScopeAsync(services => services.GetRequiredService<IPolarisCycleApi>().StartNewAsync(new DateTimeOffset(Today.ToDateTime(new TimeOnly(0, 5))), cancellationToken: Ct));

		async Task<bool> ActsAsync()
		{
			var active = (await Directive(api => api.ListActiveTimeframesAsync(TodayAt(10), Ct))).Select(record => record.Id).ToList();
			var affinity = await Vault.WithScopeAsync(services => services.GetRequiredService<TimeframeAffinityResolver>().ResolveForCollegeAsync(ObjectiveCollege.Lore, Ct));
			Assert.Equal(active.Contains(morning.Id), affinity == morning.Id);
			return active.Contains(morning.Id);
		}

		Assert.True(await ActsAsync());
		await Directive(api => api.ShiftLunarWorkflowAsync(lunar.Id, new LunarDirectiveWorkflowShift(LunarDirectiveStatus.OnHold), Ct));
		Assert.True(await ActsAsync());
		await Directive(api => api.ShiftLunarWorkflowAsync(lunar.Id, new LunarDirectiveWorkflowShift(LunarDirectiveStatus.Stale), Ct));
		Assert.False(await ActsAsync());
		await Directive(api => api.ShiftLunarWorkflowAsync(lunar.Id, new LunarDirectiveWorkflowShift(LunarDirectiveStatus.Active), Ct));
		Assert.True(await ActsAsync());
	}

	[Fact]
	public async Task A_stale_or_held_lunar_directive_leaves_its_decrees_that_do_not_reflect_alone()
	{
		var lunar = await Directive(api => api.CreateLunarAsync("Moon law", cancellationToken: Ct));
		var decree = await Declarative(api => api.CreateDecreeAsync(new DecreePlan("Stretch", DirectiveId: lunar.Id, Orbit: "d"), Ct));

		foreach (var status in new[] { LunarDirectiveStatus.Stale, LunarDirectiveStatus.OnHold })
		{
			await Directive(api => api.ShiftLunarWorkflowAsync(lunar.Id, new LunarDirectiveWorkflowShift(status), Ct));
			Assert.Contains((await Project(Today, Today.AddDays(2))).Attentives, attentive => attentive.DecreeId == decree.Id);
		}
	}
}
