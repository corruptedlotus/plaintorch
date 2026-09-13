using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The activity search unifies the two incentive kinds a Polaris cycle accepts — objectives and decrees — the way the
/// objective search does for objectives alone: id/title contains, merged into one list.
/// </summary>
public sealed class ActivitySearchTests : VaultTestBase
{
	private Task<IReadOnlyList<Activity>> FindAsync(string query)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IActivityApi>()
			.FindAsync(new SearchRequest(query), TestContext.Current.CancellationToken));

	private Task<IReadOnlyList<Activity>> ListAsync()
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IActivityApi>()
			.ListAsync(TestContext.Current.CancellationToken));

	private Task<Decree> SeedDecreeAsync(string title)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IDeclarativeApi>()
			.CreateDecreeAsync(new DecreePlan(title), TestContext.Current.CancellationToken));

	[Fact]
	public async Task Search_returns_both_matching_objectives_and_decrees()
	{
		await Vault.SeedStandaloneObjectiveAsync("Ship the release");
		await SeedDecreeAsync("Ship the newsletter");

		var results = await FindAsync("Ship");

		Assert.Equal(2, results.Count);
		Assert.Contains(results, activity => activity.Kind == "objective" && activity.Objective?.Title == "Ship the release");
		Assert.Contains(results, activity => activity.Kind == "decree" && activity.Decree?.Title == "Ship the newsletter");
	}

	[Fact]
	public async Task Search_narrows_to_one_kind_when_only_it_matches()
	{
		await Vault.SeedStandaloneObjectiveAsync("Write the report");
		await SeedDecreeAsync("Daily standup");

		var results = await FindAsync("standup");

		var activity = Assert.Single(results);
		Assert.Equal("decree", activity.Kind);
		Assert.Equal("Daily standup", activity.Decree?.Title);
	}

	[Fact]
	public async Task List_returns_every_objective_and_decree()
	{
		await Vault.SeedStandaloneObjectiveAsync("An objective");
		await SeedDecreeAsync("A decree");

		var results = await ListAsync();

		Assert.Equal(2, results.Count);
		Assert.Contains(results, activity => activity.Kind == "objective");
		Assert.Contains(results, activity => activity.Kind == "decree");
	}
}
