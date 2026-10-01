using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Materialization;
using Pleiades.Plaintorch.Preferences;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Covers lunar reflection's selection and rewards (PEP104). A beginning Polaris cycle takes every live reflect-decree
/// whose orbit lands on its day, then draws the base number of daily reflectives (a preference, 3 by default) from the
/// live reflect-decrees without an orbit. A reflect-decree is live only while it is active and every lunar directive
/// above it is active; an On Hold or Stale lunar directive pauses its whole subtree. Finishing every reflective of a
/// cycle grants the daily reward (a preference, 4 by default), and each decree-backed reflective also grants its
/// decree's per-run reward.
/// </summary>
public sealed class LunarReflectionTests : VaultTestBase
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> Directives<T>(Func<IDirectiveApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IDirectiveApi>()));

	private Task<T> Polaris<T>(Func<IPolarisCycleApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IPolarisCycleApi>()));

	private Task<T> Declaratives<T>(Func<IDeclarativeApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IDeclarativeApi>()));

	private Task SetPreferenceAsync(string key, int value)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<UserPreferenceService>().SetAsync(key, value, Ct));

	private async Task<LunarDirective> CreateLunarAsync(string title, LunarDirectiveStatus status = LunarDirectiveStatus.Active, string? parentId = null)
	{
		var lunar = await Directives(api => api.CreateLunarAsync(title, parentDirectiveId: parentId, cancellationToken: Ct));
		if (status != LunarDirectiveStatus.OnHold)
		{
			// Lunar directives start On Hold; Stale is reached through Active, as the moonlight workflow allows.
			await Directives(api => api.ShiftLunarWorkflowAsync(lunar.Id, new LunarDirectiveWorkflowShift(LunarDirectiveStatus.Active), Ct));
			if (status != LunarDirectiveStatus.Active)
			{
				await Directives(api => api.ShiftLunarWorkflowAsync(lunar.Id, new LunarDirectiveWorkflowShift(status), Ct));
			}
		}

		return lunar;
	}

	private Task<Decree> CreateReflectDecreeAsync(string title, string directiveId, string? orbit = null, int activeCelestron = 0)
		=> Declaratives(api => api.CreateDecreeAsync(new DecreePlan(title, DirectiveId: directiveId, Orbit: orbit, ActiveCelestron: activeCelestron, Reflect: true), Ct));

	private Task<List<Reflective>> ReflectivesOfAsync(string cycleId)
		=> Vault.QueryAsync(context => context.Set<Reflective>().AsNoTracking().Where(item => item.PolarisCycleId == cycleId).ToListAsync(Ct));

	private Task<int> MaterializeAgainAsync(string cycleId) => Vault.WithScopeAsync(async services =>
	{
		var cycle = await services.GetRequiredService<PlainfraContext>().PolarisCycles.SingleAsync(item => item.Id == cycleId, Ct);
		return await services.GetRequiredService<ProximityMaterializationService>().MaterializeForCycleAsync(cycle, Ct);
	});

	[Theory]
	[InlineData(LunarDirectiveStatus.OnHold)]
	[InlineData(LunarDirectiveStatus.Stale)]
	public async Task An_inactive_lunar_directive_pauses_its_reflect_decrees(LunarDirectiveStatus status)
	{
		var lunar = await CreateLunarAsync("Moon Law", status);
		var orbiting = await CreateReflectDecreeAsync("Evening reflection", lunar.Id, orbit: "d");
		var pooled = await CreateReflectDecreeAsync("Gratitude", lunar.Id);

		var cycle = await Polaris(api => api.StartNewAsync(cancellationToken: Ct));

		Assert.Empty(await ReflectivesOfAsync(cycle.Id));

		// A paused reflect-decree is still a reflect-decree: it does not fall back to projecting attentives.
		var inclusions = await Polaris(api => api.GetInclusionsAsync(null, Ct));
		Assert.DoesNotContain(inclusions.Attentives, item => item.DecreeId == orbiting.Id || item.DecreeId == pooled.Id);
	}

	[Fact]
	public async Task An_inactive_lunar_ancestor_pauses_an_active_lunar_child_and_stellar_levels_are_ignored()
	{
		var heldRoot = await CreateLunarAsync("Tide", LunarDirectiveStatus.OnHold);
		var activeChild = await CreateLunarAsync("Ebb", parentId: heldRoot.Id);
		var paused = await CreateReflectDecreeAsync("Night pages", activeChild.Id, orbit: "d");

		// A stellar directive between an active lunar root and the decree does not pause it: only lunar states count.
		var activeRoot = await CreateLunarAsync("Calm");
		var stellar = await Directives(api => api.CreateFromParentAsync(activeRoot.Id, "Garden", cancellationToken: Ct));
		var live = await CreateReflectDecreeAsync("Tend the garden", stellar.Id, orbit: "d");

		var cycle = await Polaris(api => api.StartNewAsync(cancellationToken: Ct));

		var reflective = Assert.Single(await ReflectivesOfAsync(cycle.Id));
		Assert.Equal(live.Id, reflective.DecreeId);
		Assert.NotEqual(paused.Id, reflective.DecreeId);
	}

	[Fact]
	public async Task Reactivating_the_lunar_directive_resumes_generation_on_the_next_cycle()
	{
		var lunar = await CreateLunarAsync("Moon Law", LunarDirectiveStatus.OnHold);
		var decree = await CreateReflectDecreeAsync("Evening reflection", lunar.Id, orbit: "d");

		var past = await Polaris(api => api.StartNewAsync(DateTimeOffset.Now.AddDays(-3), cancellationToken: Ct));
		await Polaris(api => api.EndAsync(past.Id, DateTimeOffset.Now.AddDays(-3).AddHours(2), Ct));
		Assert.Empty(await ReflectivesOfAsync(past.Id));

		await Directives(api => api.ShiftLunarWorkflowAsync(lunar.Id, new LunarDirectiveWorkflowShift(LunarDirectiveStatus.Active), Ct));
		var today = await Polaris(api => api.StartNewAsync(cancellationToken: Ct));

		var reflective = Assert.Single(await ReflectivesOfAsync(today.Id));
		Assert.Equal(decree.Id, reflective.DecreeId);

		// The paused day is never back-filled.
		Assert.Empty(await ReflectivesOfAsync(past.Id));
	}

	[Fact]
	public async Task An_abandoned_reflect_decree_generates_nothing()
	{
		var lunar = await CreateLunarAsync("Moon Law");
		var orbiting = await CreateReflectDecreeAsync("Evening reflection", lunar.Id, orbit: "d");
		var pooled = await CreateReflectDecreeAsync("Gratitude", lunar.Id);
		foreach (var decree in new[] { orbiting, pooled })
		{
			await Declaratives(api => api.UpdateDecreeAsync(decree.Id, new DecreeUpdate(Status: DecreeStatus.Abandoned), Ct));
		}

		var cycle = await Polaris(api => api.StartNewAsync(cancellationToken: Ct));

		Assert.Empty(await ReflectivesOfAsync(cycle.Id));
	}

	[Fact]
	public async Task A_cycle_takes_every_orbit_match_and_draws_the_base_count_from_the_orbitless_pool()
	{
		var lunar = await CreateLunarAsync("Moon Law");
		var orbiting = await CreateReflectDecreeAsync("Evening reflection", lunar.Id, orbit: "d");
		var pool = new List<string>();
		foreach (var title in new[] { "Gratitude", "Dreams", "Fears", "Hopes", "Lessons" })
		{
			pool.Add((await CreateReflectDecreeAsync(title, lunar.Id)).Id);
		}

		// An orbit-less reflect-decree outside any lunar hierarchy never joins the pool.
		var stellar = await Directives(api => api.CreateStandaloneAsync("Star Project", cancellationToken: Ct));
		var outsider = await CreateReflectDecreeAsync("Star journal", stellar.Id);

		var cycle = await Polaris(api => api.StartNewAsync(cancellationToken: Ct));

		var reflectives = await ReflectivesOfAsync(cycle.Id);
		Assert.Equal(4, reflectives.Count);
		Assert.Single(reflectives, item => item.DecreeId == orbiting.Id);
		var drawn = reflectives.Where(item => item.DecreeId != orbiting.Id).Select(item => item.DecreeId!).ToList();
		Assert.Equal(3, drawn.Distinct().Count());
		Assert.All(drawn, decreeId => Assert.Contains(decreeId, pool));
		Assert.DoesNotContain(reflectives, item => item.DecreeId == outsider.Id);

		// Each drawn reflective reads as its decree, like an orbit match.
		var titles = await Vault.QueryAsync(context => context.Decrees.IgnoreAutoIncludes()
			.Where(item => drawn.Contains(item.Id))
			.ToDictionaryAsync(item => item.Id, item => item.Title, Ct));
		Assert.All(reflectives.Where(item => item.DecreeId != orbiting.Id), item => Assert.Equal(titles[item.DecreeId!], item.Description));
	}

	[Theory]
	[InlineData(0, 0)]
	[InlineData(1, 1)]
	[InlineData(-2, 0)]
	[InlineData(5, 2)]
	public async Task The_base_daily_reflectives_preference_sets_the_pool_draw(int baseCount, int expected)
	{
		await SetPreferenceAsync(PreferenceKeys.BaseDailyReflectives, baseCount);
		var lunar = await CreateLunarAsync("Moon Law");
		var orbiting = await CreateReflectDecreeAsync("Evening reflection", lunar.Id, orbit: "d");
		await CreateReflectDecreeAsync("Gratitude", lunar.Id);
		await CreateReflectDecreeAsync("Dreams", lunar.Id);

		var cycle = await Polaris(api => api.StartNewAsync(cancellationToken: Ct));

		// The orbit match is taken whatever the base count; a pool smaller than the count is taken whole.
		var reflectives = await ReflectivesOfAsync(cycle.Id);
		Assert.Single(reflectives, item => item.DecreeId == orbiting.Id);
		Assert.Equal(expected, reflectives.Count(item => item.DecreeId != orbiting.Id));
	}

	[Fact]
	public async Task Materializing_a_cycle_again_draws_nothing_new()
	{
		var lunar = await CreateLunarAsync("Moon Law");
		await CreateReflectDecreeAsync("Evening reflection", lunar.Id, orbit: "d");
		foreach (var title in new[] { "Gratitude", "Dreams", "Fears", "Hopes", "Lessons" })
		{
			await CreateReflectDecreeAsync(title, lunar.Id);
		}

		var cycle = await Polaris(api => api.StartNewAsync(cancellationToken: Ct));
		var before = (await ReflectivesOfAsync(cycle.Id)).Select(item => item.DecreeId).OrderBy(id => id).ToList();

		Assert.Equal(0, await MaterializeAgainAsync(cycle.Id));
		var after = (await ReflectivesOfAsync(cycle.Id)).Select(item => item.DecreeId).OrderBy(id => id).ToList();
		Assert.Equal(before, after);

		// Raising the base count tops the draw up by the difference only.
		await SetPreferenceAsync(PreferenceKeys.BaseDailyReflectives, 4);
		Assert.Equal(1, await MaterializeAgainAsync(cycle.Id));
		Assert.Equal(5, (await ReflectivesOfAsync(cycle.Id)).Count);
	}

	[Fact]
	public async Task Finishing_every_reflective_grants_the_daily_reward_and_each_decree_its_per_run_reward()
	{
		var lunar = await CreateLunarAsync("Moon Law");
		var rewarding = await CreateReflectDecreeAsync("Evening reflection", lunar.Id, orbit: "d", activeCelestron: 5);
		var plain = await CreateReflectDecreeAsync("Gratitude", lunar.Id);

		var cycle = await Polaris(api => api.StartNewAsync(cancellationToken: Ct));
		var reflectives = await ReflectivesOfAsync(cycle.Id);
		var rewardingReflective = reflectives.Single(item => item.DecreeId == rewarding.Id);
		var plainReflective = reflectives.Single(item => item.DecreeId == plain.Id);

		Task<List<CelestronTransaction>> LedgerAsync() => Vault.QueryAsync(context => context.CelestronLedger.AsNoTracking()
			.Where(item => item.SourcePuck == cycle.Id || item.SourcePuck == rewarding.Id || item.SourcePuck == plain.Id)
			.ToListAsync(Ct));

		// The per-run reward lands with the run itself, before the collection is complete.
		await Polaris(api => api.UpdateReflectiveAsync(rewardingReflective.Id, new ReflectiveUpdate(Executed: true), Ct));
		var perRun = Assert.Single(await LedgerAsync());
		Assert.Equal(rewarding.Id, perRun.SourcePuck);
		Assert.Equal(5, perRun.Amount);

		// A decree without a per-run reward adds nothing of its own; completing the set grants the default daily reward.
		await Polaris(api => api.UpdateReflectiveAsync(plainReflective.Id, new ReflectiveUpdate(Executed: true), Ct));
		var ledger = await LedgerAsync();
		Assert.Equal(2, ledger.Count);
		Assert.Equal(4, Assert.Single(ledger, item => item.SourcePuck == cycle.Id).Amount);

		// Un-executing the rewarding run revokes its own reward and the daily reward, and keeps nothing else.
		await Polaris(api => api.UpdateReflectiveAsync(rewardingReflective.Id, new ReflectiveUpdate(Executed: false), Ct));
		Assert.Empty(await LedgerAsync());
	}

	[Theory]
	[InlineData(7, 7)]
	[InlineData(0, null)]
	public async Task The_daily_reward_preference_sets_the_collection_amount(int dailyReward, int? expected)
	{
		await SetPreferenceAsync(PreferenceKeys.ReflectionDailyReward, dailyReward);
		var lunar = await CreateLunarAsync("Moon Law");
		await CreateReflectDecreeAsync("Evening reflection", lunar.Id, orbit: "d");

		var cycle = await Polaris(api => api.StartNewAsync(cancellationToken: Ct));
		var reflective = Assert.Single(await ReflectivesOfAsync(cycle.Id));
		await Polaris(api => api.UpdateReflectiveAsync(reflective.Id, new ReflectiveUpdate(Executed: true), Ct));

		var collection = await Vault.QueryAsync(context => context.CelestronLedger.AsNoTracking()
			.Where(item => item.SourcePuck == cycle.Id)
			.ToListAsync(Ct));
		if (expected is { } amount)
		{
			Assert.Equal(amount, Assert.Single(collection).Amount);
		}
		else
		{
			Assert.Empty(collection);
		}
	}
}
