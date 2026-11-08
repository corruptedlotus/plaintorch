using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Enforcement;
using Pleiades.Plaintorch.Preferences;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// PEP103 I-035: a dependency names one occurrence by its exact slot, and only a slot that resolves is accepted. The structure
/// phase (SH-17) hardens only an exact, live occurrence of a temporally active fate, never the day's first occurrence in its
/// place, and the judge refuses an edge whose eventive endpoint resolves to no row (<c>dependency.occurrence</c>) at every
/// enforcement level. A hardened row deleted on its own takes the edges naming its slot with it (SH-18).
/// </summary>
public sealed class DependencyOccurrenceReferenceTests : VaultTestBase
{
	private CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> Directive<T>(Func<IDirectiveApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDirectiveApi>()));
	private Task<T> Declarative<T>(Func<IDeclarativeApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDeclarativeApi>()));
	private Task<T> Deps<T>(Func<IDependencyApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDependencyApi>()));

	private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
	private static DateTime At(DateOnly day, int hour) => day.ToDateTime(new TimeOnly(hour, 0));
	private static EndpointRef EventiveRef(string fateId, DateTime slot) => new(DependencyEndpointKind.Eventive, fateId, slot);
	private static EndpointRef DirectiveRef(string id) => new(DependencyEndpointKind.Directive, id);

	private Task<int> EventiveCount(string fateId) => Vault.QueryAsync(context => context.Eventives.CountAsync(item => item.FateId == fateId, Ct));
	private Task<int> DependencyCount() => Vault.QueryAsync(context => context.Dependencies.CountAsync(Ct));

	private async Task<StateEnforcementException> AssertRefusedAsync(EndpointRef source, string targetId)
	{
		var refused = await Assert.ThrowsAsync<StateEnforcementException>(() => Deps(api => api.CreateAsync(source, DirectiveRef(targetId), cancellationToken: Ct)));
		Assert.Contains(refused.Decision.Conflicts, conflict => conflict.Rule == StateJudge.OccurrenceRule && conflict.Kind == StateConflictKind.Systemic);
		Assert.Equal(0, await DependencyCount());
		return refused;
	}

	[Fact]
	public async Task An_inexact_slot_is_refused_and_hardens_nothing()
	{
		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Review", Orbit: "d[h{15}]", EventDuration: 30), Ct));
		var target = await Directive(api => api.CreateStandaloneAsync("Downstream", cancellationToken: Ct));

		// 14:00 is on a day the orbit covers, but nothing starts then. SH-17 used to harden the 15:00 occurrence under its
		// own slot while the edge kept 14:00, so the edge could never resolve.
		var refused = await AssertRefusedAsync(EventiveRef(fate.Id, At(Today.AddDays(5), 14)), target.Id);
		Assert.Contains("no occurrence starting at", refused.Message, StringComparison.Ordinal);
		Assert.Equal(0, await EventiveCount(fate.Id));
	}

	[Fact]
	public async Task An_off_orbit_day_is_refused()
	{
		var day = Today.AddDays(10);
		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Launch", Date: day), Ct));
		var target = await Directive(api => api.CreateStandaloneAsync("Downstream", cancellationToken: Ct));

		var refused = await AssertRefusedAsync(EventiveRef(fate.Id, day.AddDays(1).ToDateTime(TimeOnly.MinValue)), target.Id);
		Assert.Contains("no occurrence starting at", refused.Message, StringComparison.Ordinal);
		Assert.Equal(0, await EventiveCount(fate.Id));
	}

	[Fact]
	public async Task A_stopped_fates_occurrence_is_refused()
	{
		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Review", Orbit: "d[h{15}]", EventDuration: 30), Ct));
		await Declarative(api => api.UpdateFateAsync(fate.Id, new FateUpdate(Status: FateStatus.Stopped), Ct));
		var target = await Directive(api => api.CreateStandaloneAsync("Downstream", cancellationToken: Ct));

		// The slot is on the orbit, but a stopped fate generates nothing, so it would never come.
		var refused = await AssertRefusedAsync(EventiveRef(fate.Id, At(Today.AddDays(5), 15)), target.Id);
		Assert.Contains("is Stopped", refused.Message, StringComparison.Ordinal);
		Assert.Equal(0, await EventiveCount(fate.Id));
	}

	[Fact]
	public async Task An_unresolvable_slot_is_refused_even_when_enforcement_is_ignored()
	{
		await Vault.WithScopeAsync(services => services.GetRequiredService<UserPreferenceService>().SetAsync(PreferenceKeys.BacklogStateEnforcement, BacklogStateEnforcementLevel.Ignore, Ct));
		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Review", Orbit: "d[h{15}]", EventDuration: 30), Ct));
		var target = await Directive(api => api.CreateStandaloneAsync("Downstream", cancellationToken: Ct));

		await AssertRefusedAsync(EventiveRef(fate.Id, At(Today.AddDays(5), 9)), target.Id);
	}

	[Fact]
	public async Task An_exact_slot_hardens_and_is_accepted()
	{
		var day = Today.AddDays(5);
		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Review", Orbit: "d[h{15}]", EventDuration: 30), Ct));
		var target = await Directive(api => api.CreateStandaloneAsync("Downstream", cancellationToken: Ct));

		var dependency = await Deps(api => api.CreateAsync(EventiveRef(fate.Id, At(day, 15)), DirectiveRef(target.Id), cancellationToken: Ct));

		Assert.Equal(At(day, 15), dependency.SourceRecurrenceId);
		var eventive = await Vault.QueryAsync(context => context.Eventives.SingleAsync(item => item.FateId == fate.Id, Ct));
		Assert.Equal(At(day, 15), eventive.RecurrenceId);
		Assert.True((await Deps(api => api.GetLockAsync(target.Id, Ct))).BlockedBegin);
	}

	[Fact]
	public async Task A_slot_already_hardened_is_accepted_whatever_its_fate_is_now()
	{
		var day = Today.AddDays(5);
		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Review", Orbit: "d[h{15}]", EventDuration: 30), Ct));
		await Declarative(api => api.UpdateEventiveAsync(new EventiveOccurrenceRef(fate.Id, At(day, 15)), new EventiveUpdate(), Ct));
		await Declarative(api => api.UpdateFateAsync(fate.Id, new FateUpdate(Status: FateStatus.Stopped), Ct));
		var target = await Directive(api => api.CreateStandaloneAsync("Downstream", cancellationToken: Ct));

		// The row exists, so the edge resolves: stopping the fate stops what is still to be generated, not what is.
		await Deps(api => api.CreateAsync(EventiveRef(fate.Id, At(day, 15)), DirectiveRef(target.Id), cancellationToken: Ct));

		Assert.Equal(1, await DependencyCount());
		Assert.Equal(1, await EventiveCount(fate.Id));
	}

	[Fact]
	public async Task Deleting_a_hardened_row_releases_the_edges_naming_its_slot()
	{
		var day = Today.AddDays(5);
		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Review", Orbit: "d[h{15}]", EventDuration: 30), Ct));
		var target = await Directive(api => api.CreateStandaloneAsync("Downstream", cancellationToken: Ct));
		var other = await Directive(api => api.CreateStandaloneAsync("Elsewhere", cancellationToken: Ct));
		await Deps(api => api.CreateAsync(EventiveRef(fate.Id, At(day, 15)), DirectiveRef(target.Id), cancellationToken: Ct));
		await Deps(api => api.CreateAsync(EventiveRef(fate.Id, At(day.AddDays(1), 15)), DirectiveRef(target.Id), cancellationToken: Ct));
		await Deps(api => api.CreateAsync(DirectiveRef(other.Id), DirectiveRef(target.Id), cancellationToken: Ct));

		await Vault.QueryAsync(async context =>
		{
			context.Eventives.Remove(await context.Eventives.SingleAsync(item => item.FateId == fate.Id && item.RecurrenceId == At(day, 15), Ct));
			return await context.SaveChangesAsync(Ct);
		});

		var remaining = await Vault.QueryAsync(context => context.Dependencies.AsNoTracking().ToListAsync(Ct));
		Assert.Equal(2, remaining.Count);
		Assert.DoesNotContain(remaining, edge => edge.SourceRecurrenceId == At(day, 15));
		var audit = await Vault.QueryAsync(context => context.AuditLogEntries.AsNoTracking()
			.CountAsync(entry => entry.Category == "state" && entry.Action == "dependency.release", Ct));
		Assert.Equal(1, audit);
	}
}
