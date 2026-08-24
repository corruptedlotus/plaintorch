using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

public sealed class DeclarativeEcosystemTests : VaultTestBase
{
	private Task<T> WithApi<T>(Func<IDeclarativeApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IDeclarativeApi>()));

	[Fact]
	public async Task Lunar_directive_lifecycle_uses_moonlight_states()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		var lunar = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateLunarAsync("Sleep Law", cancellationToken: cancellationToken));

		Assert.StartsWith("LUNA", lunar.Id);
		Assert.Equal(7, lunar.Id.Length);
		Assert.Equal(LunarDirectiveStatus.OnHold, lunar.Status);

		var shifted = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.ShiftLunarWorkflowAsync(lunar.Id, new LunarDirectiveWorkflowShift(LunarDirectiveStatus.Active), cancellationToken));
		Assert.Equal(LunarDirectiveStatus.Active, shifted.Status);

		// The stellar workflow does not apply to lunar directives, and vice versa.
		await Assert.ThrowsAsync<InvalidOperationException>(() => Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.ShiftStellarWorkflowAsync(lunar.Id, new StellarDirectiveWorkflowShift(DirectiveStatus.Active), cancellationToken)));

		var stellar = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("Classic", cancellationToken: cancellationToken));
		await Assert.ThrowsAsync<InvalidOperationException>(() => Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.ShiftLunarWorkflowAsync(stellar.Id, new LunarDirectiveWorkflowShift(LunarDirectiveStatus.Stale), cancellationToken)));
	}

	[Fact]
	public async Task Directive_listing_filters_and_shorthands_split_by_kind()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		var stellar = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("Stellar One", cancellationToken: cancellationToken));
		var lunar = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateLunarAsync("Lunar One", cancellationToken: cancellationToken));

		var all = await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>().ListAsync(null, cancellationToken));
		Assert.Contains(all, directive => directive.Id == stellar.Id);
		Assert.Contains(all, directive => directive.Id == lunar.Id);

		var stellarFiltered = await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>().ListAsync(DirectiveKind.Stellar, cancellationToken));
		Assert.All(stellarFiltered, directive => Assert.IsType<StellarDirective>(directive));
		Assert.Contains(stellarFiltered, directive => directive.Id == stellar.Id);

		var lunarShorthand = await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>().ListLunarAsync(cancellationToken));
		Assert.Contains(lunarShorthand, directive => directive.Id == lunar.Id);
		Assert.DoesNotContain(lunarShorthand, directive => directive.Id == stellar.Id);

		var stellarShorthand = await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>().ListStellarAsync(cancellationToken));
		Assert.Contains(stellarShorthand, directive => directive.Id == stellar.Id);
		Assert.DoesNotContain(stellarShorthand, directive => directive.Id == lunar.Id);
	}

	[Fact]
	public async Task Stellar_and_lunar_updates_are_separate()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		var stellar = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("Roadmap", cancellationToken: cancellationToken));
		var due = new DateOnly(2026, 8, 1);
		var updatedStellar = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.UpdateStellarAsync(stellar.Id, new StellarDirectiveUpdate(Title: "Roadmap v2", Due: due), cancellationToken));
		Assert.Equal("Roadmap v2", updatedStellar.Title);
		Assert.Equal(due, updatedStellar.Due);

		var lunar = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateLunarAsync("Sleep", cancellationToken: cancellationToken));
		var updatedLunar = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.UpdateLunarAsync(lunar.Id, new LunarDirectiveUpdate(Title: "Sleep Well"), cancellationToken));
		Assert.Equal("Sleep Well", updatedLunar.Title);

		// A lunar directive cannot be updated through the stellar action and vice versa.
		await Assert.ThrowsAsync<InvalidOperationException>(() => Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.UpdateStellarAsync(lunar.Id, new StellarDirectiveUpdate(Title: "Nope"), cancellationToken)));
		await Assert.ThrowsAsync<InvalidOperationException>(() => Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.UpdateLunarAsync(stellar.Id, new LunarDirectiveUpdate(Title: "Nope"), cancellationToken)));
	}

	[Fact]
	public async Task Timeframes_belong_only_to_lunar_directives_and_list_globally()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		var stellar = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("Stellar", cancellationToken: cancellationToken));
		await Assert.ThrowsAsync<InvalidOperationException>(() => Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateTimeframeAsync(stellar.Id, new TimeframePlan("Nope", new TimeOnly(9, 0), new TimeOnly(10, 0)), cancellationToken)));

		var lunar = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateLunarAsync("Rhythm", cancellationToken: cancellationToken));
		var timeframe = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateTimeframeAsync(lunar.Id, new TimeframePlan("Deep Work", new TimeOnly(8, 0), new TimeOnly(12, 0)), cancellationToken));

		var all = await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>().ListAllTimeframesAsync(cancellationToken));
		var record = Assert.Single(all, item => item.Id == timeframe.Id);
		Assert.Equal(lunar.Id, record.DirectiveId);
		Assert.Equal("Rhythm", record.DirectiveTitle);
		Assert.Equal("Deep Work", record.Title);
	}

	[Fact]
	public async Task Declaratives_share_the_incentive_table_with_their_own_pucks()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		var fate = await WithApi(api => api.CreateFateAsync(new FatePlan(
			"Solstice",
			Date: new DateOnly(2026, 7, 20),
			StartTime: new TimeOnly(9, 0),
			EndTime: new TimeOnly(10, 30)), cancellationToken));
		var decree = await WithApi(api => api.CreateDecreeAsync(new DecreePlan(
			"Morning Pages",
			DefaultLength: 20,
			ActiveCelestron: 3,
			Reflect: true), cancellationToken));

		Assert.StartsWith("e", fate.Id);
		Assert.Equal(9, fate.Id.Length);
		Assert.StartsWith("r", decree.Id);
		Assert.Equal(9, decree.Id.Length);

		var incentiveCount = await Vault.QueryAsync(context => context.Incentives.IgnoreAutoIncludes().CountAsync(cancellationToken));
		Assert.Equal(2, incentiveCount);
		var objectiveCount = await Vault.QueryAsync(context => context.Objectives.IgnoreAutoIncludes().CountAsync(cancellationToken));
		Assert.Equal(0, objectiveCount);
	}

	[Fact]
	public async Task Parent_system_accepts_valid_kinds_and_exempts_decrees()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		var parentFate = await WithApi(api => api.CreateFateAsync(new FatePlan("Season"), cancellationToken));
		var childFate = await WithApi(api => api.CreateFateAsync(new FatePlan("Festival", ParentIncentiveId: parentFate.Id), cancellationToken));
		Assert.Equal(parentFate.Id, childFate.ParentIncentiveId);

		var objective = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.CreateStandaloneAsync("Prepare booth", cancellationToken: cancellationToken));
		var parented = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.UpdateAsync(objective.Id, new ObjectiveUpdate(ParentIncentiveId: parentFate.Id), cancellationToken));
		Assert.Equal(parentFate.Id, parented.ParentIncentiveId);

		var subtask = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.CreateStandaloneAsync("Order supplies", cancellationToken: cancellationToken));
		var subtasked = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.UpdateAsync(subtask.Id, new ObjectiveUpdate(ParentIncentiveId: objective.Id), cancellationToken));
		Assert.Equal(objective.Id, subtasked.ParentIncentiveId);

		// Fates cannot parent under objectives, and decrees stay exempt in both directions.
		var decree = await WithApi(api => api.CreateDecreeAsync(new DecreePlan("Routine"), cancellationToken));
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			WithApi(api => api.UpdateFateAsync(parentFate.Id, new FateUpdate(ParentIncentiveId: objective.Id), cancellationToken)));
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			WithApi(api => api.UpdateFateAsync(parentFate.Id, new FateUpdate(ParentIncentiveId: decree.Id), cancellationToken)));
		await Assert.ThrowsAsync<InvalidOperationException>(() => Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.UpdateAsync(objective.Id, new ObjectiveUpdate(ParentIncentiveId: decree.Id), cancellationToken)));
	}

	[Fact]
	public async Task Interaction_materializes_instances_without_binding_them()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		var fate = await WithApi(api => api.CreateFateAsync(new FatePlan(
			"Concert",
			Date: new DateOnly(2026, 8, 1),
			StartTime: new TimeOnly(19, 0),
			EndTime: new TimeOnly(21, 0)), cancellationToken));

		var eventive = await WithApi(api => api.MaterializeEventiveAsync(fate.Id, new EventiveMaterialization(), cancellationToken));
		Assert.Equal(new DateOnly(2026, 8, 1), eventive.Date);
		Assert.Equal(120, eventive.Estimation);
		Assert.Equal(EventiveResolution.Pending, eventive.Resolution);

		// Interacting with the same occurrence returns the existing instance instead of duplicating it.
		var again = await WithApi(api => api.MaterializeEventiveAsync(fate.Id, new EventiveMaterialization(), cancellationToken));
		Assert.Equal(eventive.Id, again.Id);

		var decree = await WithApi(api => api.CreateDecreeAsync(new DecreePlan("Journaling", DefaultLength: 15), cancellationToken));
		var attentive = await WithApi(api => api.MaterializeAttentiveAsync(decree.Id, new AttentiveMaterialization(Date: new DateOnly(2026, 8, 1)), cancellationToken));
		Assert.Null(attentive.PolarisCycleId);
		Assert.Equal(15, attentive.Estimation);

		// Eventives can always be moved because they are never Polaris-bound.
		var moved = await WithApi(api => api.UpdateEventiveAsync(eventive.Id, new EventiveUpdate(Date: new DateOnly(2026, 8, 2)), cancellationToken));
		Assert.Equal(new DateOnly(2026, 8, 2), moved.Date);

		var missed = await WithApi(api => api.UpdateEventiveAsync(eventive.Id, new EventiveUpdate(Resolution: EventiveResolution.Missed), cancellationToken));
		Assert.Equal(EventiveResolution.Missed, missed.Resolution);
	}

	[Fact]
	public async Task Attentive_mobility_honours_bound_and_unbound_rules()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		var decree = await WithApi(api => api.CreateDecreeAsync(new DecreePlan("Tidy up", DefaultLength: 10), cancellationToken));
		var unbound = await WithApi(api => api.MaterializeAttentiveAsync(decree.Id, new AttentiveMaterialization(Date: new DateOnly(2026, 8, 1)), cancellationToken));

		// Unbound: reschedule allowed, cycle moves rejected.
		var delayed = await WithApi(api => api.UpdateAttentiveAsync(unbound.Id, new AttentiveUpdate(Date: new DateOnly(2026, 8, 3)), cancellationToken));
		Assert.Equal(new DateOnly(2026, 8, 3), delayed.Date);
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			WithApi(api => api.UpdateAttentiveAsync(unbound.Id, new AttentiveUpdate(MoveToPolarisCycleId: "20260801"), cancellationToken)));

		// Bound: created by manually adding the decree to a cycle; reschedule rejected, move allowed.
		var cycle = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IPolarisCycleApi>()
			.StartNewAsync(cancellationToken: cancellationToken));
		var bound = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IPolarisCycleApi>()
			.AddDecreeAttentiveAsync(new PolarisAttentiveAdd(decree.Id), null, cancellationToken));
		Assert.Equal(cycle.Id, bound.PolarisCycleId);
		Assert.Equal(10, bound.Estimation);

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			WithApi(api => api.UpdateAttentiveAsync(bound.Id, new AttentiveUpdate(Date: new DateOnly(2026, 8, 4)), cancellationToken)));

		var forecast = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IPolarisCycleApi>()
			.PlanAsync(DateOnly.FromDateTime(DateTime.Today), 2, cancellationToken: cancellationToken));
		var movedAttentive = await WithApi(api => api.UpdateAttentiveAsync(bound.Id, new AttentiveUpdate(MoveToPolarisCycleId: forecast.Id), cancellationToken));
		Assert.Equal(forecast.Id, movedAttentive.PolarisCycleId);
	}

	[Fact]
	public async Task Attentive_execution_grants_the_decree_reward_each_time()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		var decree = await WithApi(api => api.CreateDecreeAsync(new DecreePlan("Stretch", ActiveCelestron: 3), cancellationToken));
		var first = await WithApi(api => api.MaterializeAttentiveAsync(decree.Id, new AttentiveMaterialization(Date: new DateOnly(2026, 8, 1)), cancellationToken));
		var second = await WithApi(api => api.MaterializeAttentiveAsync(decree.Id, new AttentiveMaterialization(Date: new DateOnly(2026, 8, 2)), cancellationToken));

		await WithApi(api => api.UpdateAttentiveAsync(first.Id, new AttentiveUpdate(Resolution: AttentiveResolution.Done), cancellationToken));
		await WithApi(api => api.UpdateAttentiveAsync(second.Id, new AttentiveUpdate(Resolution: AttentiveResolution.Done), cancellationToken));

		var transactions = await Vault.QueryAsync(context => context.CelestronLedger
			.Where(item => item.SourcePuck == decree.Id)
			.ToListAsync(cancellationToken));
		Assert.Equal(2, transactions.Count);
		Assert.All(transactions, transaction => Assert.Equal(3, transaction.Amount));

		// Undoing an execution revokes exactly that occurrence's reward.
		await WithApi(api => api.UpdateAttentiveAsync(second.Id, new AttentiveUpdate(Resolution: AttentiveResolution.Skipped), cancellationToken));
		var remaining = await Vault.QueryAsync(context => context.CelestronLedger
			.Where(item => item.SourcePuck == decree.Id)
			.ToListAsync(cancellationToken));
		Assert.Single(remaining);
	}

	[Fact]
	public async Task Reflective_collection_rewards_once_when_all_are_done()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		var cycle = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IPolarisCycleApi>()
			.StartNewAsync(cancellationToken: cancellationToken));
		var reflectives = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IPolarisCycleApi>()
			.DrawReflectivesAsync(new ReflectiveDrawRequest(Count: 2), cancellationToken));

		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IPolarisCycleApi>()
			.UpdateReflectiveAsync(reflectives[0].Id, new ReflectiveUpdate(Executed: true, Time: new TimeOnly(21, 0)), cancellationToken));

		var noneYet = await Vault.QueryAsync(context => context.CelestronLedger
			.Where(item => item.SourcePuck == cycle.Id)
			.CountAsync(cancellationToken));
		Assert.Equal(0, noneYet);

		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IPolarisCycleApi>()
			.UpdateReflectiveAsync(reflectives[1].Id, new ReflectiveUpdate(Executed: true), cancellationToken));

		var rewarded = await Vault.QueryAsync(context => context.CelestronLedger
			.Where(item => item.SourcePuck == cycle.Id)
			.ToListAsync(cancellationToken));
		var collection = Assert.Single(rewarded);
		Assert.True(collection.Amount > 0);

		// Un-executing a reflective revokes the collection reward.
		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IPolarisCycleApi>()
			.UpdateReflectiveAsync(reflectives[0].Id, new ReflectiveUpdate(Executed: false), cancellationToken));
		var revoked = await Vault.QueryAsync(context => context.CelestronLedger
			.Where(item => item.SourcePuck == cycle.Id)
			.CountAsync(cancellationToken));
		Assert.Equal(0, revoked);
	}

	[Fact]
	public async Task Cycle_begin_materializes_proximity_eventives_and_includes_them_non_structurally()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		var today = DateOnly.FromDateTime(DateTime.Today);

		var fate = await WithApi(api => api.CreateFateAsync(new FatePlan("Meeting", Date: today, StartTime: new TimeOnly(23, 0), EventDuration: 30), cancellationToken));
		var objective = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.CreateStandaloneAsync("Deliver", cancellationToken: cancellationToken));
		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.UpdateAsync(objective.Id, new ObjectiveUpdate(Due: today), cancellationToken));
		var decree = await WithApi(api => api.CreateDecreeAsync(new DecreePlan("Sweep"), cancellationToken));
		await WithApi(api => api.MaterializeAttentiveAsync(decree.Id, new AttentiveMaterialization(Date: today), cancellationToken));

		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IPolarisCycleApi>()
			.StartNewAsync(cancellationToken: cancellationToken));

		var eventives = await Vault.QueryAsync(context => context.Eventives.ToListAsync(cancellationToken));
		Assert.Contains(eventives, item => item.FateId == fate.Id && item.Estimation == 30);
		Assert.Contains(eventives, item => item.ObjectiveId == objective.Id);

		var inclusions = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IPolarisCycleApi>()
			.GetInclusionsAsync(null, cancellationToken));
		Assert.Contains(inclusions.Eventives, item => item.FateId == fate.Id);
		Assert.Contains(inclusions.Eventives, item => item.ObjectiveId == objective.Id);
		Assert.Contains(inclusions.Attentives, item => item.DecreeId == decree.Id);

		// Inclusion is never structural: the cycle owns no eventives relationally.
		Assert.All(inclusions.Attentives, item => Assert.Null(item.PolarisCycleId));
	}

	[Fact]
	public async Task Executives_can_name_a_timeframe_as_their_affinity()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		var lunar = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateLunarAsync("Rhythm", cancellationToken: cancellationToken));
		var timeframe = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateTimeframeAsync(lunar.Id, new TimeframePlan("Deep Work", new TimeOnly(8, 0), new TimeOnly(12, 0)), cancellationToken));

		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IPolarisCycleApi>()
			.StartNewAsync(cancellationToken: cancellationToken));
		var planned = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IPolarisCycleApi>()
			.PlanExecutiveAsync(new PolarisExecutivePlan(PolarisExecutivePlanningMode.OneShot, Title: "Focus block"), null, cancellationToken));

		var updated = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IPolarisCycleApi>()
			.UpdateExecutiveAsync(planned.Executive.Id, new ExecutiveUpdate(AffinityTimeframeId: timeframe.Id), cancellationToken));
		Assert.Equal(timeframe.Id, updated.AffinityTimeframeId);

		var cleared = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IPolarisCycleApi>()
			.UpdateExecutiveAsync(planned.Executive.Id, new ExecutiveUpdate(AffinityTimeframeId: null), cancellationToken));
		Assert.Null(cleared.AffinityTimeframeId);
	}
}
