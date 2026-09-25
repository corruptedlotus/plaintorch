using Microsoft.Extensions.DependencyInjection;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// A timeframe carries an <c>Exclusive</c> flag (PEP100 patch 2): it defaults to off, can be set at creation and toggled
/// by an update (an update that leaves it out keeps it), and travels with the timeframe in the global listing.
/// </summary>
public sealed class TimeframeExclusivityTests : VaultTestBase
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> Directives<T>(Func<IDirectiveApi, Task<T>> action)
		=> Vault.WithScopeAsync(services => action(services.GetRequiredService<IDirectiveApi>()));

	[Fact]
	public async Task Exclusive_defaults_off_can_be_set_at_creation_and_toggles_through_updates()
	{
		var lunar = await Directives(api => api.CreateLunarAsync("Moon Law", cancellationToken: Ct));
		var plain = await Directives(api => api.CreateTimeframeAsync(lunar.Id, new TimeframePlan("Morning", new TimeOnly(8, 0), new TimeOnly(12, 0)), Ct));
		var exclusive = await Directives(api => api.CreateTimeframeAsync(lunar.Id, new TimeframePlan("Deep Work", new TimeOnly(13, 0), new TimeOnly(15, 0), Exclusive: true), Ct));

		Assert.False(plain.Exclusive);
		Assert.True(exclusive.Exclusive);

		var listed = await Directives(api => api.ListAllTimeframesAsync(Ct));
		Assert.False(listed.Single(record => record.Id == plain.Id).Exclusive);
		Assert.True(listed.Single(record => record.Id == exclusive.Id).Exclusive);

		// An update that says nothing about exclusivity keeps it.
		var renamed = await Directives(api => api.UpdateTimeframeAsync(exclusive.Id, new TimeframeUpdate(Title: "Focus"), Ct));
		Assert.True(renamed.Exclusive);

		var switchedOn = await Directives(api => api.UpdateTimeframeAsync(plain.Id, new TimeframeUpdate(Exclusive: true), Ct));
		var switchedOff = await Directives(api => api.UpdateTimeframeAsync(exclusive.Id, new TimeframeUpdate(Exclusive: false), Ct));
		Assert.True(switchedOn.Exclusive);
		Assert.False(switchedOff.Exclusive);

		var relisted = await Directives(api => api.ListAllTimeframesAsync(Ct));
		Assert.True(relisted.Single(record => record.Id == plain.Id).Exclusive);
		Assert.False(relisted.Single(record => record.Id == exclusive.Id).Exclusive);
	}
}
