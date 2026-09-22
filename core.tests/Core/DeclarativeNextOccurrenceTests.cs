using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Covers the denormalized <see cref="Declarative.NextOccurrence"/> cache (PEP111): it is populated from the
/// schedule on save, reflects the next upcoming occurrence, is null when nothing is upcoming, and clears while a
/// declarative is paused.
/// </summary>
public sealed class DeclarativeNextOccurrenceTests : VaultTestBase
{
	private CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> Declarative<T>(Func<IDeclarativeApi, Task<T>> action) => Vault.WithScopeAsync(s => action(s.GetRequiredService<IDeclarativeApi>()));
	private Task<Fate> StoredFate(string id) => Vault.QueryAsync(context => context.Fates.AsNoTracking().FirstAsync(item => item.Id == id, Ct));
	private Task<Decree> StoredDecree(string id) => Vault.QueryAsync(context => context.Decrees.AsNoTracking().FirstAsync(item => item.Id == id, Ct));

	[Fact]
	public async Task A_future_one_off_fate_caches_its_single_occurrence_as_the_next()
	{
		var slot = DateOnly.FromDateTime(DateTime.Today).AddDays(10);
		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Launch", Date: slot), Ct));

		var stored = await StoredFate(fate.Id);
		Assert.Equal(slot.ToDateTime(TimeOnly.MinValue), stored.NextOccurrence);
	}

	[Fact]
	public async Task A_past_one_off_fate_has_no_next_occurrence()
	{
		var slot = DateOnly.FromDateTime(DateTime.Today).AddDays(-10);
		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Anniversary", Date: slot), Ct));

		var stored = await StoredFate(fate.Id);
		Assert.Null(stored.NextOccurrence);
	}

	[Fact]
	public async Task A_recurring_decree_caches_its_upcoming_occurrence()
	{
		var today = DateOnly.FromDateTime(DateTime.Today);
		var decree = await Declarative(api => api.CreateDecreeAsync(new DecreePlan("Standup", Orbit: "d", DefaultLength: 15), Ct));

		var stored = await StoredDecree(decree.Id);
		Assert.NotNull(stored.NextOccurrence);
		// The next daily occurrence is today's (if the run is exactly at midnight) or tomorrow's.
		Assert.InRange(stored.NextOccurrence!.Value, today.ToDateTime(TimeOnly.MinValue), today.AddDays(1).ToDateTime(TimeOnly.MinValue));
	}

	[Fact]
	public async Task Cancelling_a_fate_clears_its_next_occurrence_and_resuming_restores_it()
	{
		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Daily standup", Orbit: "d"), Ct));
		Assert.NotNull((await StoredFate(fate.Id)).NextOccurrence);

		await Declarative(api => api.UpdateFateAsync(fate.Id, new FateUpdate(Status: FateStatus.Cancelled), Ct));
		Assert.Null((await StoredFate(fate.Id)).NextOccurrence);

		await Declarative(api => api.UpdateFateAsync(fate.Id, new FateUpdate(Status: FateStatus.Active), Ct));
		Assert.NotNull((await StoredFate(fate.Id)).NextOccurrence);
	}

	[Fact]
	public async Task An_unscheduled_fate_has_no_next_occurrence()
	{
		var fate = await Declarative(api => api.CreateFateAsync(new FatePlan("Someday"), Ct));

		var stored = await StoredFate(fate.Id);
		Assert.Null(stored.NextOccurrence);
	}
}
