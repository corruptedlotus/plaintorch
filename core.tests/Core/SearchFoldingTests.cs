using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The core's free-text searches fold case and accents inside SQLite (<see cref="SearchFolding"/>) and require every
/// word of a query, in any order. They leaned on SQLite's own matching before, which is case-sensitive (<c>instr</c>)
/// or ASCII-only (<c>lower()</c>), so "pol" missed "Polaris".
/// </summary>
public sealed class SearchFoldingTests : VaultTestBase
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<IReadOnlyList<string>> FindObjectivesAsync(string query, int? take = null)
		=> Vault.WithScopeAsync<IReadOnlyList<string>>(async services => (await services.GetRequiredService<IObjectiveApi>()
			.FindAsync(new SearchRequest(query, take), Ct)).Select(objective => objective.Title).ToList());

	[Fact]
	public void Folding_drops_case_and_accents_and_leaves_other_scripts_alone()
	{
		Assert.Equal("cafe xuntas ecole", SearchFolding.Fold("Café Xuntaš ÉCOLE"));
		Assert.Equal("привет мир", SearchFolding.Fold("Привет Мир"));
		Assert.Equal("مربع", SearchFolding.Fold("مربع"));
		Assert.Null(SearchFolding.Fold(null));
		Assert.Equal(["sky", "quest"], SearchFolding.Words("  Sky   QUEST "));
		Assert.Empty(SearchFolding.Words("   "));
	}

	[Fact]
	public async Task A_context_made_outside_the_container_still_folds_inside_sqlite()
	{
		// The design-time factory and the migration tests build contexts by hand: the function must come with the context.
		var options = new DbContextOptionsBuilder<PlainfraContext>().UseSqlite("Data Source=:memory:").Options;
		await using var context = new PlainfraContext(options);

		var folded = await context.Database
			.SqlQueryRaw<string>($"SELECT {SearchFolding.FunctionName}('Café ÉCOLE') AS \"Value\"")
			.ToListAsync(Ct);

		Assert.Equal(["cafe ecole"], folded);
	}

	[Theory]
	[InlineData("pol")]
	[InlineData("POLARIS")]
	[InlineData("Polaris")]
	public async Task Objective_search_ignores_case(string query)
	{
		await Vault.SeedStandaloneObjectiveAsync("Polaris");

		Assert.Equal(["Polaris"], await FindObjectivesAsync(query));
	}

	[Theory]
	[InlineData("cafe", "Café opening")]
	[InlineData("CAFÉ", "Café opening")]
	[InlineData("xuntas", "Onrush of Xuntaš")]
	[InlineData("école", "ÉCOLE Normale")]
	public async Task Objective_search_ignores_accents_and_case_beyond_ascii(string query, string title)
	{
		await Vault.SeedStandaloneObjectiveAsync(title);

		Assert.Equal([title], await FindObjectivesAsync(query));
	}

	[Fact]
	public async Task Objective_search_requires_every_word_in_any_order()
	{
		await Vault.SeedStandaloneObjectiveAsync("Sky Quest");
		await Vault.SeedStandaloneObjectiveAsync("World Quest");
		await Vault.SeedStandaloneObjectiveAsync("Sky Garden");

		Assert.Equal(["Sky Quest"], await FindObjectivesAsync("quest sky"));
	}

	[Fact]
	public async Task Objective_search_caps_the_result_after_ordering_it_by_title()
	{
		// Ids run opposite to titles, so capping before ordering would keep "Item C" and "Item B".
		await Vault.SeedStandaloneObjectiveAsync("Item C", requestedId: "j10000001");
		await Vault.SeedStandaloneObjectiveAsync("Item B", requestedId: "j10000002");
		await Vault.SeedStandaloneObjectiveAsync("Item A", requestedId: "j10000003");

		Assert.Equal(["Item A", "Item B"], await FindObjectivesAsync("item", take: 2));
	}

	[Fact]
	public async Task Directive_search_ignores_case()
	{
		var directive = await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("Searchable Directive", cancellationToken: Ct));

		var found = await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.FindAsync(new SearchRequest("searchable directive"), Ct));

		Assert.Equal([directive.Id], found.Select(hit => hit.Id));
	}

	[Fact]
	public async Task Activity_search_folds_case_beyond_ascii()
	{
		// SQLite's lower() leaves "É" alone, which the activity search used to lean on.
		await Vault.WithScopeAsync(services => services.GetRequiredService<IDeclarativeApi>()
			.CreateDecreeAsync(new DecreePlan("ÉVALUER le budget"), Ct));
		await Vault.SeedStandaloneObjectiveAsync("Weekly review");

		var found = await Vault.WithScopeAsync(services => services.GetRequiredService<IActivityApi>()
			.FindAsync(new SearchRequest("évaluer"), Ct));

		var activity = Assert.Single(found);
		Assert.Equal("decree", activity.Kind);
	}

	[Fact]
	public async Task Endpoint_search_ignores_case_and_accents()
	{
		var checkpoint = await Vault.WithScopeAsync(services => services.GetRequiredService<IDependencyApi>()
			.CreateCheckpointAsync("Café Gate", cancellationToken: Ct));

		var hits = await Vault.WithScopeAsync(services => services.GetRequiredService<IDependencyApi>()
			.SearchEndpointsAsync(new SearchRequest("gate cafe"), Ct));

		var hit = Assert.Single(hits);
		Assert.Equal((DependencyEndpointKind.Checkpoint, checkpoint.Id), (hit.Kind, hit.Id));
	}

	[Fact]
	public async Task Endpoint_listing_returns_every_endpoint()
	{
		// More than the 20 per kind an empty search used to stop at: the picker now matches the whole listing itself.
		for (var index = 1; index <= 21; index++)
		{
			await Vault.SeedStandaloneObjectiveAsync($"Objective {index:00}");
		}

		var hits = await Vault.WithScopeAsync(services => services.GetRequiredService<IDependencyApi>()
			.ListEndpointsAsync(Ct));

		Assert.Equal(21, hits.Count(hit => hit.Kind == DependencyEndpointKind.Objective));
	}
}
