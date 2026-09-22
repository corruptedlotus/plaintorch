using Microsoft.Extensions.DependencyInjection;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// A decree-backed executive carries a timeframe affinity (PEP111): it can be seeded when the decree is added to a
/// cycle, set or cleared afterwards, and a timeframe that does not exist is refused.
/// </summary>
public sealed class DecreeExecutiveAffinityTests : VaultTestBase
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

		var executive = await Polaris(api => api.AddDecreeExecutiveAsync(new PolarisDecreeAdd(decree.Id, AffinityTimeframeId: timeframeId), cancellationToken: Ct));

		Assert.Equal(timeframeId, executive.AffinityTimeframeId);
	}

	[Fact]
	public async Task A_decree_executive_affinity_can_be_set_and_cleared()
	{
		var timeframeId = await CreateTimeframeAsync();
		var decree = await Declarative(api => api.CreateDecreeAsync(new DecreePlan("Journal"), Ct));
		var executive = await Polaris(api => api.AddDecreeExecutiveAsync(new PolarisDecreeAdd(decree.Id), cancellationToken: Ct));

		var affined = await Polaris(api => api.UpdateExecutiveAsync(executive.Id, new ExecutiveUpdate(AffinityTimeframeId: timeframeId), Ct));
		Assert.Equal(timeframeId, affined.AffinityTimeframeId);

		var cleared = await Polaris(api => api.UpdateExecutiveAsync(executive.Id, new ExecutiveUpdate(AffinityTimeframeId: (long?)null), Ct));
		Assert.Null(cleared.AffinityTimeframeId);
	}

	[Fact]
	public async Task An_unknown_timeframe_is_refused()
	{
		var decree = await Declarative(api => api.CreateDecreeAsync(new DecreePlan("Walk"), Ct));
		var executive = await Polaris(api => api.AddDecreeExecutiveAsync(new PolarisDecreeAdd(decree.Id), cancellationToken: Ct));

		await Assert.ThrowsAsync<InvalidOperationException>(() => Polaris(api =>
			api.UpdateExecutiveAsync(executive.Id, new ExecutiveUpdate(AffinityTimeframeId: 999_999L), Ct)));
	}
}
