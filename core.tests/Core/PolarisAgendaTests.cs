using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
			.AddDecreeAttentiveAsync(new PolarisAttentiveAdd(DecreeId: boundDecree.Id), cycle.Id, ct));

		var loaded = await Vault.WithScopeAsync(s => s.GetRequiredService<IPolarisCycleApi>()
			.GetAsync(cycle.Id, ct));

		Assert.NotNull(loaded);

		var reflective = Assert.Single(loaded!.Reflectives);
		Assert.Equal(lunarDecree.Id, reflective.DecreeId);
		Assert.NotNull(reflective.Decree);
		Assert.NotNull(reflective.Decree!.Directive);
		Assert.Equal(lunar.Id, reflective.Decree!.Directive!.Id);

		var attentive = Assert.Single(loaded.Attentives);
		Assert.Equal(cycle.Id, attentive.PolarisCycleId);
		Assert.NotNull(attentive.Decree);
		Assert.NotNull(attentive.Decree!.Directive);
		Assert.Equal(stellar.Id, attentive.Decree!.Directive!.Id);
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

		// A cycle only exists so a bound attentive has a real foreign key to hang on. The decree has no orbit,
		// so beginning the cycle seeds nothing, keeping the agenda entirely from the rows inserted below.
		var cycle = await Vault.WithScopeAsync(s => s.GetRequiredService<IPolarisCycleApi>()
			.StartNewAsync(cancellationToken: ct));

		await Vault.WithScopeAsync(async s =>
		{
			var context = s.GetRequiredService<PlainfraContext>();

			context.Add(new Fate { Id = "agenda-test-fate", Title = "Conference", DirectiveId = directive.Id });

			context.Attentives.AddRange(
				new Attentive { DecreeId = decree.Id, Date = today, Resolution = AttentiveResolution.Pending },
				new Attentive { DecreeId = decree.Id, Date = today.AddDays(-1), Resolution = AttentiveResolution.Pending },
				// Resolved two hours ago: Done and outside the one-hour retention window, so it stays excluded.
				new Attentive { DecreeId = decree.Id, Date = today, Resolution = AttentiveResolution.Done, ResolvedOn = DateTimeOffset.UtcNow.AddHours(-2) },
				new Attentive { DecreeId = decree.Id, Date = today.AddDays(2), Resolution = AttentiveResolution.Pending },
				new Attentive { DecreeId = decree.Id, Date = today, Resolution = AttentiveResolution.Pending, PolarisCycleId = cycle.Id });

			context.Eventives.AddRange(
				new Eventive { FateId = "agenda-test-fate", Date = today.AddDays(3), RecurrenceDate = today.AddDays(3), Resolution = EventiveResolution.Pending },
				new Eventive { FateId = "agenda-test-fate", Date = today.AddDays(10), RecurrenceDate = today.AddDays(10), Resolution = EventiveResolution.Pending });

			await context.SaveChangesAsync(ct);
		});

		var agenda = await Vault.WithScopeAsync(s => s.GetRequiredService<IPolarisCycleApi>()
			.GetAgendaAsync(ct));

		// Unbound, pending, and due today or earlier: the done, the future, and the bound rows are all excluded.
		Assert.Equal(2, agenda.Attentives.Count);
		Assert.All(agenda.Attentives, attentive =>
		{
			Assert.Null(attentive.PolarisCycleId);
			Assert.Equal(AttentiveResolution.Pending, attentive.Resolution);
			Assert.True(attentive.Date <= today);
			Assert.NotNull(attentive.Decree);
		});

		// Upcoming within the seven-day horizon; the far-future occurrence is excluded.
		var eventive = Assert.Single(agenda.Eventives);
		Assert.Equal(today.AddDays(3), eventive.Date);
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
		var cycle = await Vault.WithScopeAsync(s => s.GetRequiredService<IPolarisCycleApi>()
			.StartNewAsync(cancellationToken: ct));

		var recentlyResolvedOn = DateTimeOffset.UtcNow.AddMinutes(-30);
		await Vault.WithScopeAsync(async s =>
		{
			var context = s.GetRequiredService<PlainfraContext>();
			context.Attentives.AddRange(
				new Attentive
				{
					DecreeId = decree.Id,
					Date = today.AddDays(14),
					Resolution = AttentiveResolution.Done,
					ResolvedOn = recentlyResolvedOn,
				},
				new Attentive
				{
					DecreeId = decree.Id,
					PolarisCycleId = cycle.Id,
					Date = today.AddDays(14),
					Resolution = AttentiveResolution.Done,
					ResolvedOn = recentlyResolvedOn,
				},
				new Attentive
				{
					DecreeId = decree.Id,
					Date = today.AddDays(14),
					Resolution = AttentiveResolution.Done,
					ResolvedOn = DateTimeOffset.UtcNow.AddHours(-2),
				});
			await context.SaveChangesAsync(ct);
		});

		var agenda = await Vault.WithScopeAsync(s => s.GetRequiredService<IPolarisCycleApi>()
			.GetAgendaAsync(ct));

		var attentive = Assert.Single(agenda.Attentives);
		Assert.Null(attentive.PolarisCycleId);
		Assert.Equal(recentlyResolvedOn, attentive.ResolvedOn);
	}
}
