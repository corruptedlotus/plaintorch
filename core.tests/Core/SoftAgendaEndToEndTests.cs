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
/// End-to-end coverage of the soft-agenda model (Strategy 1): an orbiting schedule forecasts live without
/// persisting, an occurrence hardens when interacted with or referenced, time-based hardening catches up the
/// past, and changing the orbit leaves the hardened occurrences intact and still referenceable while the
/// forecast recomputes to the new orbit — dropping every un-hardened old-orbit occurrence.
/// </summary>
public sealed class SoftAgendaEndToEndTests : VaultTestBase
{
	private CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> Directive<T>(Func<IDirectiveApi, Task<T>> action) => Vault.WithScopeAsync(s => action(s.GetRequiredService<IDirectiveApi>()));
	private Task<T> Declarative<T>(Func<IDeclarativeApi, Task<T>> action) => Vault.WithScopeAsync(s => action(s.GetRequiredService<IDeclarativeApi>()));
	private Task<T> Deps<T>(Func<IDependencyApi, Task<T>> action) => Vault.WithScopeAsync(s => action(s.GetRequiredService<IDependencyApi>()));
	private Task Deps(Func<IDependencyApi, Task> action) => Vault.WithScopeAsync(s => action(s.GetRequiredService<IDependencyApi>()));
	private Task<PolarisAgenda> Agenda() => Vault.WithScopeAsync(s => s.GetRequiredService<IPolarisCycleApi>().GetAgendaAsync(Ct));
	private Task<int> HardenAt(DateOnly day, int hour) => Vault.WithScopeAsync(s => s.GetRequiredService<ProximityMaterializationService>()
		.MaterializeForNowAsync(new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, 0))), Ct));
	private Task<AgendaProjection> Project(DateOnly start, DateOnly end) => Vault.WithScopeAsync(s => s.GetRequiredService<AgendaProjectionService>().ProjectAsync(start, end, Ct));

	private static EndpointRef DirectiveRef(string id) => new(DependencyEndpointKind.Directive, id);
	private static EndpointRef EventiveRef(string ownerId, DateOnly date, TimeOnly? time = null) => new(DependencyEndpointKind.Eventive, ownerId, date, time);

	[Fact]
	public async Task Referencing_a_projected_occurrence_hardens_it_into_a_row()
	{
		var slot = DateOnly.FromDateTime(DateTime.Today).AddDays(20);
		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Milestone", Date: slot, StartTime: new TimeOnly(14, 0), EventDuration: 45), Ct));

		// Strategy 1: creating the fate persists nothing — its occurrence is a pure projection.
		var before = await Vault.QueryAsync(context => context.Eventives.CountAsync(item => item.FateId == fate.Id, Ct));
		Assert.Equal(0, before);

		// Referencing that occurrence as a dependency endpoint must harden it (deep-interception enforcement),
		// so the reconciler resolves a real row rather than reading an absent projection as unsatisfied.
		var downstream = await Directive(api => api.CreateStandaloneAsync("Downstream", cancellationToken: Ct));
		await Deps(api => api.CreateAsync(EventiveRef(fate.Id, slot, new TimeOnly(14, 0)), DirectiveRef(downstream.Id), cancellationToken: Ct));

		var eventive = await Vault.QueryAsync(context => context.Eventives.SingleAsync(item => item.FateId == fate.Id && item.RecurrenceDate == slot, Ct));
		Assert.Equal(new TimeOnly(14, 0), eventive.RecurrenceTime);
		Assert.Equal(45, eventive.Estimation);

		// The now-real, still-pending source occurrence gates the target.
		var lockView = await Deps(api => api.GetLockAsync(downstream.Id, Ct));
		Assert.True(lockView.BlockedBegin);
	}

	[Fact]
	public async Task Orbiting_forecast_then_manual_hardening_then_time_advance_then_orbit_change()
	{
		var today = DateOnly.FromDateTime(DateTime.Today);
		var farAttentiveDay = today.AddDays(20);
		var farEventiveDay = today.AddDays(25);

		// 1. An orbiting decree (daily 09:00 → attentives) and an orbiting fate (daily 15:00 → eventives).
		var decree = await Declarative(api => api.CreateDecreeAsync(new DecreePlan("Standup", Orbit: "d[h{9}]", DefaultLength: 15), Ct));
		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Review", Orbit: "d[h{15}]", EventDuration: 30), Ct));

		// 2. Forecast forward: the agenda projects the upcoming occurrences with nothing persisted yet.
		var forecast = await Agenda();
		Assert.Contains(forecast.Attentives, item => item.DecreeId == decree.Id && item.RecurrenceTime == new TimeOnly(9, 0));
		Assert.Contains(forecast.Eventives, item => item.FateId == fate.Id && item.RecurrenceTime == new TimeOnly(15, 0));
		var persistedBefore = await Vault.QueryAsync(context => context.Attentives.CountAsync(Ct));
		Assert.Equal(0, persistedBefore);

		// 3a. Modify a far-future occurrence: interacting hardens the decree's day+20 attentive into a row.
		var farAttentive = await Declarative(api => api.UpdateAttentiveAsync(new AttentiveOccurrenceRef(decree.Id, farAttentiveDay), new AttentiveUpdate(), Ct));
		Assert.Equal(new TimeOnly(9, 0), farAttentive.RecurrenceTime);

		// 3b. Reference another far-future occurrence: a dependency hardens the fate's day+25 eventive.
		var downstream = await Directive(api => api.CreateStandaloneAsync("Downstream", cancellationToken: Ct));
		await Deps(api => api.CreateAsync(EventiveRef(fate.Id, farEventiveDay, new TimeOnly(15, 0)), DirectiveRef(downstream.Id), cancellationToken: Ct));

		// Both far-future occurrences are now durable rows.
		var hardenedAttentiveId = await Vault.QueryAsync(context => context.Attentives
			.Where(item => item.DecreeId == decree.Id && item.RecurrenceDate == farAttentiveDay).Select(item => item.Id).SingleAsync(Ct));
		Assert.True(hardenedAttentiveId > 0);
		var hardenedEventive = await Vault.QueryAsync(context => context.Eventives.SingleAsync(item => item.FateId == fate.Id && item.RecurrenceDate == farEventiveDay, Ct));
		Assert.Equal(new TimeOnly(15, 0), hardenedEventive.RecurrenceTime);

		// 4. Advance time part-way (day+10 noon) — not far enough to reach the two manual cases. Normal
		//    time-based hardening catches up the intervening occurrences.
		await HardenAt(today.AddDays(10), 12);
		var midDay = today.AddDays(5);
		var midHardened = await Vault.QueryAsync(context => context.Attentives.CountAsync(item => item.DecreeId == decree.Id && item.RecurrenceDate == midDay, Ct));
		Assert.Equal(1, midHardened);
		// The two manual far-future occurrences are untouched by the tick (still exactly one each).
		var farStillOne = await Vault.QueryAsync(context => context.Attentives.CountAsync(item => item.DecreeId == decree.Id && item.RecurrenceDate == farAttentiveDay, Ct));
		Assert.Equal(1, farStillOne);

		// 5. Change the decree's orbit (09:00 → 17:00).
		await Declarative(api => api.UpdateDecreeAsync(decree.Id, new DecreeUpdate(Orbit: "d[h{17}]"), Ct));

		// 5a. The hardened far-future attentive remains as-is (same row, same slot).
		var survivor = await Vault.QueryAsync(context => context.Attentives.SingleAsync(item => item.DecreeId == decree.Id && item.RecurrenceDate == farAttentiveDay, Ct));
		Assert.Equal(hardenedAttentiveId, survivor.Id);
		Assert.Equal(new TimeOnly(9, 0), survivor.RecurrenceTime);

		// 5b. The referenced fate eventive is still referenceable — the dependency still gates the target.
		var lockView = await Deps(api => api.GetLockAsync(downstream.Id, Ct));
		Assert.True(lockView.BlockedBegin);

		// 5c. The forecast now shows only NEW-orbit (17:00) occurrences plus the hardened old-orbit ones: every
		//     remaining 09:00 occurrence is a hardened row (Id > 0); no un-hardened old-orbit projection survives.
		var projection = await Project(today, today.AddDays(30));
		Assert.Contains(projection.Attentives, item => item.DecreeId == decree.Id && item.RecurrenceTime == new TimeOnly(17, 0));
		Assert.Contains(projection.Attentives, item => item.DecreeId == decree.Id && item.RecurrenceDate == farAttentiveDay && item.RecurrenceTime == new TimeOnly(9, 0));
		var oldOrbitOccurrences = projection.Attentives
			.Where(item => item.DecreeId == decree.Id && item.RecurrenceTime == new TimeOnly(9, 0))
			.ToList();
		Assert.NotEmpty(oldOrbitOccurrences);
		Assert.All(oldOrbitOccurrences, item => Assert.True(item.Id > 0));
	}
}
