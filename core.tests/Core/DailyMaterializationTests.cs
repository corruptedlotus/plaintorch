using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Materialization;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Covers the daily, cycle-independent materialization pass that backs the agenda: an orbit decree's
/// attentive is materialized (and reaches the agenda) without any Polaris cycle being begun, reflect-decrees
/// are left for cycle begin, and fate eventives fill the upcoming horizon.
/// </summary>
public sealed class DailyMaterializationTests : VaultTestBase
{
	private Task<int> MaterializeDayAsync(DateOnly today, int horizonDays = 7)
		=> Vault.WithScopeAsync(services => services
			.GetRequiredService<ProximityMaterializationService>()
			.MaterializeForDayAsync(today, horizonDays));

	[Fact]
	public async Task Creating_an_orbit_decree_materializes_todays_attentive_and_reaches_the_agenda_without_a_cycle()
	{
		var ct = TestContext.Current.CancellationToken;
		var today = DateOnly.FromDateTime(DateTime.Today);

		var directive = await Vault.WithScopeAsync(s => s.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("Ops", cancellationToken: ct));

		// Creating the decree rechecks materialization, so today's unbound attentive exists immediately —
		// no cycle begun, no explicit pass.
		var decree = await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>()
			.CreateDecreeAsync(new DecreePlan("Standup", DirectiveId: directive.Id, Orbit: "d", DefaultLength: 15), ct));

		var attentive = await Vault.QueryAsync(context => context.Attentives
			.SingleAsync(item => item.DecreeId == decree.Id && item.Date == today, ct));
		Assert.Null(attentive.PolarisCycleId);
		Assert.Equal(AttentiveResolution.Pending, attentive.Resolution);

		var agenda = await Vault.WithScopeAsync(s => s.GetRequiredService<IPolarisCycleApi>().GetAgendaAsync(ct));
		Assert.Contains(agenda.Attentives, item => item.DecreeId == decree.Id && item.Date == today);
	}

	[Fact]
	public async Task Changing_a_decree_orbit_rechecks_and_materializes_the_new_todays_attentive()
	{
		var ct = TestContext.Current.CancellationToken;
		var today = DateOnly.FromDateTime(DateTime.Today);

		// Born orbit-less: nothing to materialize, so no attentive exists yet.
		var decree = await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>()
			.CreateDecreeAsync(new DecreePlan("Tidy", DefaultLength: 10), ct));
		var before = await Vault.QueryAsync(context => context.Attentives
			.CountAsync(item => item.DecreeId == decree.Id, ct));
		Assert.Equal(0, before);

		// Assigning a daily orbit rechecks and materializes today's attentive.
		await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>()
			.UpdateDecreeAsync(decree.Id, new DecreeUpdate(Orbit: "d"), ct));

		var attentive = await Vault.QueryAsync(context => context.Attentives
			.SingleAsync(item => item.DecreeId == decree.Id && item.Date == today, ct));
		Assert.Null(attentive.PolarisCycleId);
	}

	[Fact]
	public async Task Daily_pass_is_idempotent_and_does_not_duplicate_attentives()
	{
		var ct = TestContext.Current.CancellationToken;
		var today = DateOnly.FromDateTime(DateTime.Today);

		var decree = await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>()
			.CreateDecreeAsync(new DecreePlan("Hydrate", Orbit: "d", DefaultLength: 5), ct));

		await MaterializeDayAsync(today);
		await MaterializeDayAsync(today);

		var count = await Vault.QueryAsync(context => context.Attentives
			.CountAsync(item => item.DecreeId == decree.Id && item.Date == today, ct));
		Assert.Equal(1, count);
	}

	[Fact]
	public async Task Daily_pass_leaves_reflect_lunar_decrees_for_cycle_begin()
	{
		var ct = TestContext.Current.CancellationToken;
		var today = DateOnly.FromDateTime(DateTime.Today);

		var lunar = await Vault.WithScopeAsync(s => s.GetRequiredService<IDirectiveApi>()
			.CreateLunarAsync("Moon Law", cancellationToken: ct));
		var reflectDecree = await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>()
			.CreateDecreeAsync(new DecreePlan("Evening reflection", DirectiveId: lunar.Id, Orbit: "d", Reflect: true), ct));

		await MaterializeDayAsync(today);

		// A reflect-decree in a lunar hierarchy produces neither an attentive nor a reflective on the daily
		// pass — its reflective is cycle-bound and belongs to cycle begin.
		var attentiveCount = await Vault.QueryAsync(context => context.Attentives
			.CountAsync(item => item.DecreeId == reflectDecree.Id, ct));
		Assert.Equal(0, attentiveCount);
		var reflectiveCount = await Vault.QueryAsync(context => context.Set<Reflective>()
			.CountAsync(item => item.DecreeId == reflectDecree.Id, ct));
		Assert.Equal(0, reflectiveCount);
	}

	[Fact]
	public async Task Daily_pass_fills_the_upcoming_eventive_horizon()
	{
		var ct = TestContext.Current.CancellationToken;
		var today = DateOnly.FromDateTime(DateTime.Today);

		await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>()
			.CreateFateAsync(new FatePlan("Daily standup", Orbit: "d", EventDuration: 30), ct));

		await MaterializeDayAsync(today, horizonDays: 7);

		var agenda = await Vault.WithScopeAsync(s => s.GetRequiredService<IPolarisCycleApi>().GetAgendaAsync(ct));
		Assert.NotEmpty(agenda.Eventives);
		Assert.All(agenda.Eventives, eventive => Assert.True(eventive.Date >= today && eventive.Date <= today.AddDays(7)));
	}
}
