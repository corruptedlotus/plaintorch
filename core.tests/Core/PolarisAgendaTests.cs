using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orbits;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Covers the read-shaping this feature adds: reflectives and Polaris-bound attentives (with their decree's
/// directive) served on the current cycle, and the day-level agenda of unbound attentives requiring
/// attention and upcoming eventives.
/// </summary>
public sealed class PolarisAgendaTests : VaultTestBase
{
	[Fact]
	public async Task Active_cycle_serves_reflectives_and_bound_attentives_with_directives()
	{
		var ct = TestContext.Current.CancellationToken;

		// A lunar reflect decree yields a cycle-bound reflective on begin; its decree carries the lunar directive.
		var lunar = await Vault.WithScopeAsync(s => s.GetRequiredService<IDirectiveApi>()
			.CreateLunarAsync("Moon Law", cancellationToken: ct));
		var lunarDecree = await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>()
			.CreateDecreeAsync(new DecreePlan("Evening reflection", DirectiveId: lunar.Id, Orbit: "d", Reflect: true), ct));

		// An orbit-less decree under a stellar directive never auto-seeds; it is bound to the cycle explicitly.
		var stellar = await Vault.WithScopeAsync(s => s.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("Star Project", cancellationToken: ct));
		var boundDecree = await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>()
			.CreateDecreeAsync(new DecreePlan("Standup", DirectiveId: stellar.Id), ct));

		var cycle = await Vault.WithScopeAsync(s => s.GetRequiredService<IPolarisCycleApi>()
			.StartNewAsync(cancellationToken: ct));

		await Vault.WithScopeAsync(s => s.GetRequiredService<IPolarisCycleApi>()
			.AddDecreeExecutiveAsync(new PolarisDecreeAdd(DecreeId: boundDecree.Id), cycle.Id, ct));

		var loaded = await Vault.WithScopeAsync(s => s.GetRequiredService<IPolarisCycleApi>()
			.GetAsync(cycle.Id, ct));

		Assert.NotNull(loaded);

		var reflective = Assert.Single(loaded!.Reflectives);
		Assert.Equal(lunarDecree.Id, reflective.DecreeId);
		Assert.NotNull(reflective.Decree);
		Assert.NotNull(reflective.Decree!.Directive);
		Assert.Equal(lunar.Id, reflective.Decree!.Directive!.Id);

		// The decree bound to the cycle is a decree-backed executive, carrying its incentive and directive.
		var executive = Assert.Single(loaded.Executives);
		Assert.Equal(boundDecree.Id, executive.IncentiveId);
		Assert.NotNull(executive.Incentive);
		Assert.NotNull(executive.Incentive!.Directive);
		Assert.Equal(stellar.Id, executive.Incentive!.Directive!.Id);
	}

	[Fact]
	public async Task Agenda_projects_orbit_occurrences_carrying_their_owning_directive()
	{
		// A projected occurrence (never hardened) still has to surface its owner's directive: the plugin renders
		// the directive line from decree.directive / fate.directive. The projection reads its owners with
		// IgnoreAutoIncludes, so the directive must be re-included explicitly or it comes back null.
		var ct = TestContext.Current.CancellationToken;

		var directive = await Vault.WithScopeAsync(s => s.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("Ops", cancellationToken: ct));

		// A daily orbit decree projects an unbound attentive today; a daily orbit fate projects eventives across
		// the horizon. Neither is hardened, so both exercise the projected read path.
		await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>()
			.CreateDecreeAsync(new DecreePlan("Check inbox", DirectiveId: directive.Id, Orbit: "d"), ct));
		await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>()
			.CreateFateAsync(new FatePlan("Daily sync", DirectiveId: directive.Id, Orbit: "d"), ct));

		var agenda = await Vault.WithScopeAsync(s => s.GetRequiredService<IPolarisCycleApi>()
			.GetAgendaAsync(ct));

		Assert.NotEmpty(agenda.Attentives);
		Assert.All(agenda.Attentives, attentive =>
		{
			Assert.NotNull(attentive.Decree);
			Assert.NotNull(attentive.Decree!.Directive);
			Assert.Equal(directive.Id, attentive.Decree!.Directive!.Id);
		});

		Assert.NotEmpty(agenda.Eventives);
		Assert.All(agenda.Eventives, eventive =>
		{
			Assert.NotNull(eventive.Fate);
			Assert.NotNull(eventive.Fate!.Directive);
			Assert.Equal(directive.Id, eventive.Fate!.Directive!.Id);
		});
	}

	[Fact]
	public async Task Agenda_lists_unbound_pending_due_attentives_and_upcoming_eventives()
	{
		var ct = TestContext.Current.CancellationToken;
		var today = DateOnly.FromDateTime(DateTime.Today);

		var directive = await Vault.WithScopeAsync(s => s.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("Ops", cancellationToken: ct));
		var decree = await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>()
			.CreateDecreeAsync(new DecreePlan("Check inbox", DirectiveId: directive.Id), ct));

		await Vault.WithScopeAsync(async s =>
		{
			var context = s.GetRequiredService<PlainfraContext>();

			context.Add(new Fate { Id = "agenda-test-fate", Title = "Conference", DirectiveId = directive.Id });

			context.Attentives.AddRange(
				new Attentive { DecreeId = decree.Id, Epoch = Epoch.From(today, null, OrbitUnit.Day), Resolution = AttentiveResolution.Pending },
				new Attentive { DecreeId = decree.Id, Epoch = Epoch.From(today.AddDays(-1), null, OrbitUnit.Day), Resolution = AttentiveResolution.Pending },
				// Resolved two hours ago: Done and outside the one-hour retention window, so it stays excluded.
				new Attentive { DecreeId = decree.Id, Epoch = Epoch.From(today, null, OrbitUnit.Day), Resolution = AttentiveResolution.Done, ResolvedOn = DateTimeOffset.UtcNow.AddHours(-2) },
				new Attentive { DecreeId = decree.Id, Epoch = Epoch.From(today.AddDays(2), null, OrbitUnit.Day), Resolution = AttentiveResolution.Pending });

			context.Eventives.AddRange(
				new Eventive { FateId = "agenda-test-fate", Epoch = Epoch.From(today.AddDays(3), null, OrbitUnit.Day), RecurrenceId = today.AddDays(3).ToDateTime(TimeOnly.MinValue), Resolution = EventiveResolution.Pending },
				new Eventive { FateId = "agenda-test-fate", Epoch = Epoch.From(today.AddDays(10), null, OrbitUnit.Day), RecurrenceId = today.AddDays(10).ToDateTime(TimeOnly.MinValue), Resolution = EventiveResolution.Pending });

			await context.SaveChangesAsync(ct);
		});

		var agenda = await Vault.WithScopeAsync(s => s.GetRequiredService<IPolarisCycleApi>()
			.GetAgendaAsync(ct));

		// Pending and due today or earlier: the done and the future rows are excluded.
		Assert.Equal(2, agenda.Attentives.Count);
		Assert.All(agenda.Attentives, attentive =>
		{
			Assert.Equal(AttentiveResolution.Pending, attentive.Resolution);
			Assert.True(attentive.Epoch.Date <= today);
			Assert.NotNull(attentive.Decree);
		});

		// Upcoming within the seven-day horizon; the far-future occurrence is excluded.
		var eventive = Assert.Single(agenda.Eventives);
		Assert.Equal(today.AddDays(3), eventive.Epoch.Date);
		Assert.NotNull(eventive.Fate);
	}

	[Fact]
	public async Task Agenda_includes_only_unbound_attentives_resolved_within_the_past_hour()
	{
		var ct = TestContext.Current.CancellationToken;
		var today = DateOnly.FromDateTime(DateTime.Today);

		var directive = await Vault.WithScopeAsync(s => s.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("Ops", cancellationToken: ct));
		var decree = await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>()
			.CreateDecreeAsync(new DecreePlan("Check inbox", DirectiveId: directive.Id), ct));

		var recentlyResolvedOn = DateTimeOffset.UtcNow.AddMinutes(-30);
		await Vault.WithScopeAsync(async s =>
		{
			var context = s.GetRequiredService<PlainfraContext>();
			context.Attentives.AddRange(
				new Attentive
				{
					DecreeId = decree.Id,
					Epoch = Epoch.From(today.AddDays(14), null, OrbitUnit.Day),
					Resolution = AttentiveResolution.Done,
					ResolvedOn = recentlyResolvedOn,
				},
				new Attentive
				{
					DecreeId = decree.Id,
					Epoch = Epoch.From(today.AddDays(14), null, OrbitUnit.Day),
					Resolution = AttentiveResolution.Done,
					ResolvedOn = DateTimeOffset.UtcNow.AddHours(-2),
				});
			await context.SaveChangesAsync(ct);
		});

		var agenda = await Vault.WithScopeAsync(s => s.GetRequiredService<IPolarisCycleApi>()
			.GetAgendaAsync(ct));

		var attentive = Assert.Single(agenda.Attentives);
		Assert.Equal(recentlyResolvedOn, attentive.ResolvedOn);
	}
}
