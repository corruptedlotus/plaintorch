using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

public sealed class OrbitDeclarativeTests : VaultTestBase
{
	private Task<T> WithApi<T>(Func<IDeclarativeApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IDeclarativeApi>()));

	[Fact]
	public async Task Orbit_notations_are_validated_per_declarative_kind()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		// Fates accept any granularity — including span-format orbits, which carry the event length.
		var spanFate = await WithApi(api => api.CreateFateAsync(new FatePlan("Workday", Orbit: "d[h{9}]=8h"), cancellationToken));
		Assert.Equal("d[h{9}]=8h", spanFate.Orbit);

		// Decrees accept any granularity but must stay granular (lengths come from their default length).
		var timedDecree = await WithApi(api => api.CreateDecreeAsync(new DecreePlan("Hourly", Orbit: "d[h{9,17}]"), cancellationToken));
		Assert.Equal("d[h{9,17}]", timedDecree.Orbit);
		await Assert.ThrowsAsync<ArgumentException>(() =>
			WithApi(api => api.CreateDecreeAsync(new DecreePlan("Bad", Orbit: "d=8h"), cancellationToken)));
		await Assert.ThrowsAsync<ArgumentException>(() =>
			WithApi(api => api.CreateDecreeAsync(new DecreePlan("Bad", Orbit: "nonsense!"), cancellationToken)));

		// Reflecting decrees are the strict case: their orbits must resolve at day granularity.
		await Assert.ThrowsAsync<ArgumentException>(() =>
			WithApi(api => api.CreateDecreeAsync(new DecreePlan("Bad", Orbit: "d[h{9}]", Reflect: true), cancellationToken)));
		var reflectDecree = await WithApi(api => api.CreateDecreeAsync(new DecreePlan("Nightly", Orbit: "d", Reflect: true), cancellationToken));
		Assert.Equal("d", reflectDecree.Orbit);

		// Toggling reflect on later re-validates the combination.
		await Assert.ThrowsAsync<ArgumentException>(() =>
			WithApi(api => api.UpdateDecreeAsync(timedDecree.Id, new DecreeUpdate(Reflect: true), cancellationToken)));
	}

	[Fact]
	public async Task Decree_orbits_resolve_on_the_pleiadean_calendar()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		var today = DateOnly.FromDateTime(DateTime.Today);

		// "First day of every month" means Pleiadean months for decrees: 61-day cadence, not Gregorian.
		var decree = await WithApi(api => api.CreateDecreeAsync(new DecreePlan("Monthly rite", Orbit: "M[d{1}]"), cancellationToken));

		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IPolarisCycleApi>()
			.StartNewAsync(cancellationToken: cancellationToken));

		var attentives = await Vault.QueryAsync(context => context.Attentives
			.Where(item => item.DecreeId == decree.Id)
			.ToListAsync(cancellationToken));

		// The seek covers [state epoch, window end): at most one Pleiadean month boundary can fall in there,
		// and it must NOT be a Gregorian month start unless the calendars coincide.
		Assert.True(attentives.Count <= 1);
		foreach (var attentive in attentives)
		{
			var pleiadean = Pleiades.Calendar.PleiadeanCalendar.FromDateTime(attentive.Epoch.Date.ToDateTime(TimeOnly.MinValue));
			Assert.Equal(1, pleiadean.Day);
		}

		_ = today;
	}

	[Fact]
	public async Task Cycle_begin_projects_orbit_schedules_into_its_inclusions()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		var today = DateOnly.FromDateTime(DateTime.Today);

		var fate = await WithApi(api => api.CreateFateAsync(new FatePlan("Daily fate", Orbit: "d", EventDuration: 45), cancellationToken));
		var decree = await WithApi(api => api.CreateDecreeAsync(new DecreePlan("Daily decree", Orbit: "d", DefaultLength: 20), cancellationToken));

		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IPolarisCycleApi>()
			.StartNewAsync(cancellationToken: cancellationToken));

		// Strategy 1: cycle begin persists no orbit instances — the 24h window is projected. The daily fate's
		// eventive and the daily decree's unbound attentive appear in the cycle's inclusions, not as rows.
		var inclusions = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IPolarisCycleApi>()
			.GetInclusionsAsync(null, cancellationToken));
		Assert.Contains(inclusions.Eventives, item => item.FateId == fate.Id && item.Epoch.Date == today && item.Estimation == 45);
		Assert.Contains(inclusions.Attentives, item => item.DecreeId == decree.Id && item.Epoch.Date == today && item.Estimation == 20 && item.PolarisCycleId == null);

		var persistedEventives = await Vault.QueryAsync(context => context.Eventives.CountAsync(item => item.FateId == fate.Id, cancellationToken));
		Assert.Equal(0, persistedEventives);
	}

	[Fact]
	public async Task Lunar_reflect_decrees_generate_cycle_bound_reflectives()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		var lunar = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateLunarAsync("Moon Law", cancellationToken: cancellationToken));
		var lunarDecree = await WithApi(api => api.CreateDecreeAsync(new DecreePlan(
			"Evening reflection", DirectiveId: lunar.Id, Orbit: "d", Reflect: true), cancellationToken));

		var stellar = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("Star Project", cancellationToken: cancellationToken));
		var stellarDecree = await WithApi(api => api.CreateDecreeAsync(new DecreePlan(
			"Star routine", DirectiveId: stellar.Id, Orbit: "d", Reflect: true), cancellationToken));

		var cycle = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IPolarisCycleApi>()
			.StartNewAsync(cancellationToken: cancellationToken));

		// Lunar hierarchy + reflect => a reflective bound to the cycle, carrying decree provenance.
		var reflective = await Vault.QueryAsync(context => context.Set<Reflective>()
			.SingleAsync(item => item.DecreeId == lunarDecree.Id, cancellationToken));
		Assert.Equal(cycle.Id, reflective.PolarisCycleId);
		Assert.False(reflective.Executed);

		// Stellar hierarchies cannot participate in moonlight reflection: the decree projects an unbound
		// attentive into the cycle instead (Strategy 1 — projected, not hardened).
		var stellarReflectives = await Vault.QueryAsync(context => context.Set<Reflective>()
			.CountAsync(item => item.DecreeId == stellarDecree.Id, cancellationToken));
		Assert.Equal(0, stellarReflectives);
		var inclusions = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IPolarisCycleApi>()
			.GetInclusionsAsync(null, cancellationToken));
		Assert.Contains(inclusions.Attentives, item => item.DecreeId == stellarDecree.Id && item.PolarisCycleId == null);
	}

	[Fact]
	public async Task Interaction_previews_orbit_occurrences_without_seeking_and_seek_recognizes_them()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		var today = DateOnly.FromDateTime(DateTime.Today);

		var decree = await WithApi(api => api.CreateDecreeAsync(new DecreePlan("Alternating", Orbit: "d%2", DefaultLength: 10), cancellationToken));

		// The orbit anchors today, so tomorrow is off-phase: interaction must reject it.
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			WithApi(api => api.UpdateAttentiveAsync(new AttentiveOccurrenceRef(decree.Id, today.AddDays(1)), new AttentiveUpdate(), cancellationToken)));

		// A future on-phase occurrence resolves through preview without advancing the schedule.
		var future = await WithApi(api => api.UpdateAttentiveAsync(new AttentiveOccurrenceRef(decree.Id, today.AddDays(2)), new AttentiveUpdate(), cancellationToken));
		Assert.Equal(today.AddDays(2), future.Epoch.Date);

		// Interacting with today's occurrence too, then beginning a cycle: the seeking pass must recognize
		// the already-hardened instance by its date instead of duplicating it.
		await WithApi(api => api.UpdateAttentiveAsync(new AttentiveOccurrenceRef(decree.Id, today), new AttentiveUpdate(), cancellationToken));
		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IPolarisCycleApi>()
			.StartNewAsync(cancellationToken: cancellationToken));

		var todayCount = await Vault.QueryAsync(context => context.Attentives
			.CountAsync(item => item.DecreeId == decree.Id && item.Epoch.Moment >= today.ToDateTime(TimeOnly.MinValue) && item.Epoch.Moment < today.AddDays(1).ToDateTime(TimeOnly.MinValue), cancellationToken));
		Assert.Equal(1, todayCount);
	}

	[Fact]
	public async Task Declaratives_follow_the_implicit_vault_policy()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		var fate = await WithApi(api => api.CreateFateAsync(new FatePlan("Quiet Event", Orbit: "w[d{2}]"), cancellationToken));

		// Implicit storage: creation does not materialize a file.
		Assert.False(Vault.VaultFileExists("Fates/Quiet Event.md"));

		// Beginning the boundary materializes the quiet (title-only) file with frontmatter identity.
		await WithApi(api => api.BeginFateBoundaryAsync(fate.Id, cancellationToken));
		Assert.True(Vault.VaultFileExists("Fates/Quiet Event.md"));
		var markdown = Vault.ReadVaultFile("Fates/Quiet Event.md");
		Assert.Contains($"puck: {fate.Id}", markdown);
		// Values containing '[' are JSON-escaped by the frontmatter serializer.
		Assert.Contains("orbit: \"w[d{2}]\"", markdown);

		// Deleting through the API removes the materialized file.
		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDeclarativeApi>()
			.DeleteFateAsync(fate.Id, cancellationToken));
		Assert.False(Vault.VaultFileExists("Fates/Quiet Event.md"));

		// Decrees mirror the same policy under their own root.
		var decree = await WithApi(api => api.CreateDecreeAsync(new DecreePlan("Quiet Routine"), cancellationToken));
		Assert.False(Vault.VaultFileExists("Decrees/Quiet Routine.md"));
		await WithApi(api => api.BeginDecreeBoundaryAsync(decree.Id, cancellationToken));
		Assert.True(Vault.VaultFileExists("Decrees/Quiet Routine.md"));
	}
}
