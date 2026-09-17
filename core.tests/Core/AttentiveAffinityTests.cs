using Microsoft.Extensions.DependencyInjection;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// An attentive carries a timeframe affinity like an executive: it can be seeded when the decree is added to a cycle,
/// set or cleared afterwards, and a timeframe that does not exist is refused.
/// </summary>
public sealed class AttentiveAffinityTests : VaultTestBase
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> Declarative<T>(Func<IDeclarativeApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IDeclarativeApi>()));

	private Task<T> Polaris<T>(Func<IPolarisCycleApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IPolarisCycleApi>()));

	private async Task<long> CreateTimeframeAsync()
	{
		var lunar = await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.CreateLunarAsync("Moon Law", cancellationToken: Ct));
		var timeframe = await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.CreateTimeframeAsync(lunar.Id, new TimeframePlan("Morning", new TimeOnly(8, 0), new TimeOnly(12, 0)), Ct));
		return timeframe.Id;
	}

	[Fact]
	public async Task Adding_a_decree_to_the_cycle_seeds_the_requested_affinity()
	{
		var timeframeId = await CreateTimeframeAsync();
		var decree = await Declarative(api => api.CreateDecreeAsync(new DecreePlan("Stretch"), Ct));

		var attentive = await Polaris(api => api.AddDecreeAttentiveAsync(new PolarisAttentiveAdd(decree.Id, AffinityTimeframeId: timeframeId), cancellationToken: Ct));

		Assert.Equal(timeframeId, attentive.AffinityTimeframeId);
	}

	[Fact]
	public async Task An_attentive_affinity_can_be_set_and_cleared()
	{
		var timeframeId = await CreateTimeframeAsync();
		var decree = await Declarative(api => api.CreateDecreeAsync(new DecreePlan("Journal"), Ct));
		var attentive = await Polaris(api => api.AddDecreeAttentiveAsync(new PolarisAttentiveAdd(decree.Id), cancellationToken: Ct));
		var occurrence = new AttentiveOccurrenceRef(Id: attentive.Id);

		var affined = await Declarative(api => api.UpdateAttentiveAsync(occurrence, new AttentiveUpdate(AffinityTimeframeId: timeframeId), Ct));
		Assert.Equal(timeframeId, affined.AffinityTimeframeId);

		var cleared = await Declarative(api => api.UpdateAttentiveAsync(occurrence, new AttentiveUpdate(AffinityTimeframeId: (long?)null), Ct));
		Assert.Null(cleared.AffinityTimeframeId);
	}

	[Fact]
	public async Task An_unknown_timeframe_is_refused()
	{
		var decree = await Declarative(api => api.CreateDecreeAsync(new DecreePlan("Walk"), Ct));
		var attentive = await Polaris(api => api.AddDecreeAttentiveAsync(new PolarisAttentiveAdd(decree.Id), cancellationToken: Ct));

		await Assert.ThrowsAsync<InvalidOperationException>(() => Declarative(api =>
			api.UpdateAttentiveAsync(new AttentiveOccurrenceRef(Id: attentive.Id), new AttentiveUpdate(AffinityTimeframeId: 999_999L), Ct)));
	}
}
