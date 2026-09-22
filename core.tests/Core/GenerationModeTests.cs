using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Materialization;
using Pleiades.Plaintorch.Preferences;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Covers the declarative generation modes (PEP100/PEP111). An OPTED-OUT fate keeps generating but its
/// occurrences spawn hidden (<see cref="EventiveResolution.OptOut"/>) and are not hardened by time passage unless
/// the preference opts in. A CANCELLED fate and an ABANDONED decree pause generation entirely; re-activating one
/// seeks its schedule cursor to now, so the occurrences that elapsed while it was paused are never back-filled.
/// </summary>
public sealed class GenerationModeTests : VaultTestBase
{
	private CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> Declarative<T>(Func<IDeclarativeApi, Task<T>> action) => Vault.WithScopeAsync(s => action(s.GetRequiredService<IDeclarativeApi>()));
	private Task<PolarisAgenda> Agenda() => Vault.WithScopeAsync(s => s.GetRequiredService<IPolarisCycleApi>().GetAgendaAsync(Ct));
	private Task<AgendaProjection> Project(DateOnly start, DateOnly end) => Vault.WithScopeAsync(s => s.GetRequiredService<AgendaProjectionService>().ProjectAsync(start, end, Ct));
	private Task<int> HardenAt(DateTimeOffset now) => Vault.WithScopeAsync(s => s.GetRequiredService<ProximityMaterializationService>().MaterializeForNowAsync(now, Ct));

	private Task SetHardenOptOutOnTimePassage(bool value) => Vault.WithScopeAsync(s =>
		s.GetRequiredService<UserPreferenceService>().SetAsync(PreferenceKeys.AutoMaterialiseOptOut, value, Ct));

	private static DateTimeOffset TodayAt(int hour, int minute = 0)
		=> new(DateOnly.FromDateTime(DateTime.Today).ToDateTime(new TimeOnly(hour, minute)));

	[Fact]
	public async Task An_opted_out_fate_keeps_projecting_its_occurrences_stamped_optout_and_hidden_from_the_agenda()
	{
		var today = DateOnly.FromDateTime(DateTime.Today);

		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Daily standup", Orbit: "d", EventDuration: 30), Ct));
		await Declarative(api => api.UpdateFateAsync(fate.Id, new FateUpdate(Status: FateStatus.OptOut), Ct));

		// The opted-out fate still generates: its occurrences remain live projections, but every projected
		// eventive is stamped OptOut.
		var projection = await Project(today, today.AddDays(7));
		var projected = projection.Eventives.Where(item => item.FateId == fate.Id).ToList();
		Assert.NotEmpty(projected);
		Assert.All(projected, eventive => Assert.Equal(EventiveResolution.OptOut, eventive.Resolution));

		// The display agenda shows only pending occurrences, so the opted-out fate is hidden there.
		var agenda = await Agenda();
		Assert.DoesNotContain(agenda.Eventives, eventive => eventive.FateId == fate.Id);
	}

	[Fact]
	public async Task Opted_out_occurrences_are_not_hardened_by_time_by_default_but_are_stamped_optout_when_the_preference_opts_in()
	{
		var today = DateOnly.FromDateTime(DateTime.Today);

		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Daily standup", Orbit: "d", EventDuration: 30), Ct));
		await Declarative(api => api.UpdateFateAsync(fate.Id, new FateUpdate(Status: FateStatus.OptOut), Ct));

		// Default preference (off): the passage of time hardens nothing for an opted-out fate — its occurrences
		// stay projections.
		var createdWhileOff = await HardenAt(TodayAt(23));
		Assert.Equal(0, createdWhileOff);
		var persistedWhileOff = await Vault.QueryAsync(context => context.Eventives.CountAsync(item => item.FateId == fate.Id, Ct));
		Assert.Equal(0, persistedWhileOff);

		// With the preference on, the same pass hardens the elapsed occurrence — but stamped OptOut so it stays
		// hidden rather than surfacing as a plain pending event.
		await SetHardenOptOutOnTimePassage(true);
		var createdWhileOn = await HardenAt(TodayAt(23));
		Assert.Equal(1, createdWhileOn);

		var eventive = await Vault.QueryAsync(context => context.Eventives
			.SingleAsync(item => item.FateId == fate.Id && item.RecurrenceDate == today, Ct));
		Assert.Equal(EventiveResolution.OptOut, eventive.Resolution);
	}

	[Fact]
	public async Task Cancelling_a_fate_pauses_generation_and_resuming_it_seeks_the_cursor_to_now_without_backfilling()
	{
		var today = DateOnly.FromDateTime(DateTime.Today);

		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Daily standup", Orbit: "d", EventDuration: 30), Ct));
		await Declarative(api => api.UpdateFateAsync(fate.Id, new FateUpdate(Status: FateStatus.Cancelled), Ct));

		// Paused: an end-of-day pass hardens nothing, even though today's occurrence is already in the past.
		var createdWhilePaused = await HardenAt(TodayAt(23));
		Assert.Equal(0, createdWhilePaused);

		// Re-activating seeks the cursor to now, so today's elapsed occurrence is skipped rather than back-filled:
		// the same end-of-day pass still hardens nothing.
		await Declarative(api => api.UpdateFateAsync(fate.Id, new FateUpdate(Status: FateStatus.Active), Ct));
		var createdAfterResume = await HardenAt(TodayAt(23));
		Assert.Equal(0, createdAfterResume);

		var persisted = await Vault.QueryAsync(context => context.Eventives.CountAsync(item => item.FateId == fate.Id, Ct));
		Assert.Equal(0, persisted);

		// The fate is genuinely active again: its future occurrences still project live.
		var projection = await Project(today, today.AddDays(7));
		Assert.Contains(projection.Eventives, eventive => eventive.FateId == fate.Id);
	}

	[Fact]
	public async Task Abandoning_a_decree_pauses_generation_and_resuming_it_seeks_the_cursor_to_now_without_backfilling()
	{
		var today = DateOnly.FromDateTime(DateTime.Today);

		var decree = await Declarative(api => api.CreateDecreeAsync(new DecreePlan("Standup", Orbit: "d", DefaultLength: 15), Ct));
		await Declarative(api => api.UpdateDecreeAsync(decree.Id, new DecreeUpdate(Status: DecreeStatus.Abandoned), Ct));

		// Paused: an end-of-day pass hardens nothing, even though today's occurrence is already in the past.
		var createdWhilePaused = await HardenAt(TodayAt(23));
		Assert.Equal(0, createdWhilePaused);

		// Re-activating seeks the cursor to now, so today's elapsed occurrence is skipped rather than back-filled.
		await Declarative(api => api.UpdateDecreeAsync(decree.Id, new DecreeUpdate(Status: DecreeStatus.Active), Ct));
		var createdAfterResume = await HardenAt(TodayAt(23));
		Assert.Equal(0, createdAfterResume);

		var persisted = await Vault.QueryAsync(context => context.Attentives.CountAsync(item => item.DecreeId == decree.Id, Ct));
		Assert.Equal(0, persisted);

		// The decree is genuinely active again: its occurrences still project live.
		var projection = await Project(today, today.AddDays(7));
		Assert.Contains(projection.Attentives, attentive => attentive.DecreeId == decree.Id);
	}
}
