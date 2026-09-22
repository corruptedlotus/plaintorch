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
/// Covers the soft-agenda model (Strategy 1): declaring an orbit projects its occurrences into the agenda
/// live (nothing is persisted on create/change), and the rolling harden-on-time pass persists only the
/// occurrences whose time has arrived — the catch-up of an occurrence sitting after the cursor and before
/// now being the crucial case. Reflect-decrees stay for cycle begin; when time is not an interaction the pass
/// hardens nothing.
/// </summary>
public sealed class RollingMaterializationTests : VaultTestBase
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<int> HardenNowAsync(DateTimeOffset? now = null)
		=> Vault.WithScopeAsync(services => services
			.GetRequiredService<ProximityMaterializationService>()
			.MaterializeForNowAsync(now ?? DateTimeOffset.Now, Ct));

	private Task<PolarisAgenda> AgendaAsync()
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IPolarisCycleApi>().GetAgendaAsync(Ct));

	private Task SetTimeIsInteractionAsync(bool value)
		=> Vault.WithScopeAsync(services =>
		{
			services.GetRequiredService<MaterializationPolicyOptions>().TimeIsInteraction = value;
			return Task.CompletedTask;
		});

	private static DateTimeOffset TodayAt(int hour, int minute = 0)
		=> new(DateOnly.FromDateTime(DateTime.Today).ToDateTime(new TimeOnly(hour, minute)));

	[Fact]
	public async Task Creating_an_orbit_decree_projects_todays_attentive_without_persisting_or_a_cycle()
	{
		var today = DateOnly.FromDateTime(DateTime.Today);

		var directive = await Vault.WithScopeAsync(s => s.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("Ops", cancellationToken: Ct));
		var decree = await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>()
			.CreateDecreeAsync(new DecreePlan("Standup", DirectiveId: directive.Id, Orbit: "d", DefaultLength: 15), Ct));

		// Declaring the schedule persists nothing — the occurrence is a live projection until it is interacted
		// with or its time arrives.
		var persisted = await Vault.QueryAsync(context => context.Attentives.CountAsync(item => item.DecreeId == decree.Id, Ct));
		Assert.Equal(0, persisted);

		var agenda = await AgendaAsync();
		Assert.Contains(agenda.Attentives, item => item.DecreeId == decree.Id && item.Epoch.Date == today);
	}

	[Fact]
	public async Task Changing_a_decree_orbit_reflects_in_the_agenda_projection_immediately()
	{
		var today = DateOnly.FromDateTime(DateTime.Today);

		var decree = await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>()
			.CreateDecreeAsync(new DecreePlan("Tidy", DefaultLength: 10), Ct));
		var before = await AgendaAsync();
		Assert.DoesNotContain(before.Attentives, item => item.DecreeId == decree.Id);

		await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>()
			.UpdateDecreeAsync(decree.Id, new DecreeUpdate(Orbit: "d"), Ct));

		var after = await AgendaAsync();
		Assert.Contains(after.Attentives, item => item.DecreeId == decree.Id && item.Epoch.Date == today);
	}

	[Fact]
	public async Task Harden_on_time_is_idempotent_and_does_not_duplicate_attentives()
	{
		var today = DateOnly.FromDateTime(DateTime.Today);

		var decree = await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>()
			.CreateDecreeAsync(new DecreePlan("Hydrate", Orbit: "d", DefaultLength: 5), Ct));

		await HardenNowAsync();
		await HardenNowAsync();

		var count = await Vault.QueryAsync(context => context.Attentives
			.CountAsync(item => item.DecreeId == decree.Id && item.RecurrenceDate == today, Ct));
		Assert.Equal(1, count);
	}

	[Fact]
	public async Task Harden_on_time_leaves_reflect_lunar_decrees_for_cycle_begin()
	{
		var lunar = await Vault.WithScopeAsync(s => s.GetRequiredService<IDirectiveApi>()
			.CreateLunarAsync("Moon Law", cancellationToken: Ct));
		var reflectDecree = await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>()
			.CreateDecreeAsync(new DecreePlan("Evening reflection", DirectiveId: lunar.Id, Orbit: "d", Reflect: true), Ct));

		await HardenNowAsync();

		var attentiveCount = await Vault.QueryAsync(context => context.Attentives
			.CountAsync(item => item.DecreeId == reflectDecree.Id, Ct));
		Assert.Equal(0, attentiveCount);
		var reflectiveCount = await Vault.QueryAsync(context => context.Set<Reflective>()
			.CountAsync(item => item.DecreeId == reflectDecree.Id, Ct));
		Assert.Equal(0, reflectiveCount);
	}

	[Fact]
	public async Task Upcoming_fate_eventives_are_projected_across_the_agenda_horizon_without_a_pass()
	{
		var today = DateOnly.FromDateTime(DateTime.Today);

		await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>()
			.CreateFateAsync(new FatePlan("Daily standup", Orbit: "d", EventDuration: 30), Ct));

		// No pass: the horizon is a projection, not pre-filled rows.
		var agenda = await AgendaAsync();
		Assert.NotEmpty(agenda.Eventives);
		Assert.All(agenda.Eventives, eventive => Assert.True(eventive.Epoch.Date >= today && eventive.Epoch.Date <= today.AddDays(7)));

		var persisted = await Vault.QueryAsync(context => context.Eventives.CountAsync(Ct));
		Assert.Equal(0, persisted);
	}

	[Fact]
	public async Task A_past_dated_fate_hardens_on_time_and_is_not_duplicated()
	{
		var pastDay = DateOnly.FromDateTime(DateTime.Today).AddDays(-5);

		var fate = await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>()
			.CreateFateAsync(new FatePlan("Backdated", Date: pastDay, StartTime: new TimeOnly(9, 0), EventDuration: 30), Ct));

		// A dated fate whose occurrence is already in the past hardens on the next pass; a second pass must not
		// duplicate it.
		await HardenNowAsync();
		await HardenNowAsync();

		var eventive = await Vault.QueryAsync(context => context.Eventives
			.SingleAsync(item => item.FateId == fate.Id && item.RecurrenceDate == pastDay, Ct));
		Assert.Equal(pastDay, eventive.RecurrenceDate);
	}

	[Fact]
	public async Task An_overdue_objective_hardens_on_time()
	{
		var pastDue = DateOnly.FromDateTime(DateTime.Today).AddDays(-5);

		var objective = await Vault.WithScopeAsync(s => s.GetRequiredService<IObjectiveApi>()
			.CreateStandaloneAsync("Overdue deliverable", cancellationToken: Ct));
		await Vault.WithScopeAsync(s => s.GetRequiredService<IObjectiveApi>()
			.UpdateAsync(objective.Id, new ObjectiveUpdate(Due: Due.On(pastDue)), Ct));

		await HardenNowAsync();

		var eventive = await Vault.QueryAsync(context => context.Eventives
			.SingleAsync(item => item.ObjectiveId == objective.Id && item.RecurrenceDate == pastDue, Ct));
		Assert.Equal(pastDue, eventive.Epoch.Date);
	}

	[Fact]
	public async Task An_occurrence_after_the_cursor_and_before_now_is_hardened()
	{
		var today = DateOnly.FromDateTime(DateTime.Today);

		// Daily at 09:00. The schedule's epoch is today, so its first occurrence is today 09:00.
		var decree = await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>()
			.CreateDecreeAsync(new DecreePlan("Standup", Orbit: "d[h{9}]", DefaultLength: 15), Ct));

		// A tick at 06:00 is before the 09:00 occurrence: nothing hardens, and the cursor advances to 06:00.
		var createdEarly = await HardenNowAsync(TodayAt(6));
		Assert.Equal(0, createdEarly);
		var afterEarly = await Vault.QueryAsync(context => context.Attentives.CountAsync(item => item.DecreeId == decree.Id, Ct));
		Assert.Equal(0, afterEarly);

		// A tick at 12:00: the 09:00 occurrence now lies AFTER the cursor (06:00) and BEFORE now (12:00), so it
		// is the catch-up the pass must harden. Basic but crucial.
		var createdLate = await HardenNowAsync(TodayAt(12));
		Assert.Equal(1, createdLate);

		var attentive = await Vault.QueryAsync(context => context.Attentives
			.SingleAsync(item => item.DecreeId == decree.Id && item.RecurrenceDate == today, Ct));
		Assert.Equal(new TimeOnly(9, 0), attentive.RecurrenceTime);
	}

	[Fact]
	public async Task Harden_on_time_does_not_harden_still_future_occurrences()
	{
		// Daily at 21:00; a midday tick must leave it a projection, not a row.
		var decree = await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>()
			.CreateDecreeAsync(new DecreePlan("Evening", Orbit: "d[h{21}]", DefaultLength: 10), Ct));

		var created = await HardenNowAsync(TodayAt(12));
		Assert.Equal(0, created);

		var persisted = await Vault.QueryAsync(context => context.Attentives.CountAsync(item => item.DecreeId == decree.Id, Ct));
		Assert.Equal(0, persisted);

		var agenda = await AgendaAsync();
		Assert.Contains(agenda.Attentives, item => item.DecreeId == decree.Id && item.RecurrenceTime == new TimeOnly(21, 0));
	}

	[Fact]
	public async Task When_time_is_not_an_interaction_the_pass_hardens_nothing_but_the_agenda_still_projects()
	{
		var today = DateOnly.FromDateTime(DateTime.Today);
		await SetTimeIsInteractionAsync(false);

		var decree = await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>()
			.CreateDecreeAsync(new DecreePlan("Stretch", Orbit: "d", DefaultLength: 5), Ct));

		var created = await HardenNowAsync(TodayAt(23));
		Assert.Equal(0, created);

		var persisted = await Vault.QueryAsync(context => context.Attentives.CountAsync(item => item.DecreeId == decree.Id, Ct));
		Assert.Equal(0, persisted);

		var agenda = await AgendaAsync();
		Assert.Contains(agenda.Attentives, item => item.DecreeId == decree.Id && item.Epoch.Date == today);
	}
}
