using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Hosting;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Writes and reads inside a nested hierarchy — a campaign directive, a strike directive under it, objectives and a
/// decree under both, members of an onrush and a Polaris executive — serialize as the host sends them, and carry the
/// directive each incentive belongs to.
/// <para>
/// Every incentive is read with its directive (auto-included), and a write returns its tracked entity, which EF has
/// fixed up into one graph with everything else the request loaded: the objective, its directive, that directive's
/// incentives, its parent and sub-directives. That graph is cyclic by construction, and the host's serializer must
/// cut each cycle (<see cref="System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles"/>) instead of throwing
/// on it. What a client does with the cycles its identity map builds is its own guard (the SDK's
/// <c>objectiveUpdateCycle.test.ts</c>).
/// </para>
/// </summary>
public sealed class NestedHierarchySerializationTests : VaultTestBase
{
	private static readonly JsonSerializerOptions HostOptions = CreateHostOptions();

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private static JsonSerializerOptions CreateHostOptions()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		PlaintorchHostFactory.ConfigureSerializer(options);
		return options;
	}

	/// <summary>Serializes as an endpoint returning <typeparamref name="T"/> would, failing with the serializer's own message.</summary>
	private static JsonNode Wire<T>(T value)
	{
		string json;
		try
		{
			json = JsonSerializer.Serialize(value, HostOptions);
		}
		catch (JsonException exception)
		{
			Assert.Fail($"{typeof(T).Name} did not serialize: {exception.Message}");
			throw;
		}

		return JsonNode.Parse(json)!;
	}

	private Task<T> Objectives<T>(Func<IObjectiveApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IObjectiveApi>()));

	private Task<T> Directives<T>(Func<IDirectiveApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDirectiveApi>()));

	private sealed record Hierarchy(StellarDirective Campaign, StellarDirective Strike, Objective Landing, Objective Survey, Objective Beacon, Decree Standup, OnrushSprint Sprint);

	private async Task<Hierarchy> SeedAsync()
	{
		var campaign = await Directives(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Ct));
		var strike = await Directives(api => api.CreateFromParentAsync(campaign.Id, "Strike", cancellationToken: Ct));
		var sprint = await Vault.WithScopeAsync(services => services.GetRequiredService<IOnrushSprintApi>().StartNewAsync(cancellationToken: Ct));
		var landing = await Objectives(api => api.CreateFromDirectiveAsync(strike.Id, "Landing", sprint.Id, cancellationToken: Ct));
		var survey = await Objectives(api => api.CreateFromDirectiveAsync(strike.Id, "Survey", cancellationToken: Ct));
		var beacon = await Objectives(api => api.CreateFromDirectiveAsync(campaign.Id, "Beacon", sprint.Id, cancellationToken: Ct));
		var standup = await Vault.WithScopeAsync(services => services.GetRequiredService<IDeclarativeApi>().CreateDecreeAsync(new DecreePlan("Standup", DirectiveId: strike.Id), Ct));
		await Vault.WithScopeAsync(services => services.GetRequiredService<IPolarisCycleApi>().PlanExecutiveAsync(
			new PolarisExecutivePlan(PolarisExecutivePlanningMode.FromObjective, ObjectiveId: landing.Id, Estimation: 60), cancellationToken: Ct));
		return new Hierarchy(campaign, strike, landing, survey, beacon, standup, sprint);
	}

	[Fact]
	public async Task Editing_an_objective_under_a_sub_directive_returns_a_serializable_graph_with_its_directive()
	{
		var hierarchy = await SeedAsync();

		var renamed = await Objectives(api => api.UpdateAsync(hierarchy.Landing.Id, new ObjectiveUpdate(Title: "Landing, revised"), Ct));
		var wire = Wire(renamed);

		Assert.Equal("Landing, revised", wire["title"]?.GetValue<string>());
		Assert.Equal(hierarchy.Strike.Id, wire["directive"]?["id"]?.GetValue<string>());
	}

	[Fact]
	public async Task Moving_an_objective_to_another_directive_returns_the_directive_it_moved_to()
	{
		var hierarchy = await SeedAsync();

		var moved = await Objectives(api => api.UpdateAsync(hierarchy.Survey.Id, new ObjectiveUpdate(DirectiveId: hierarchy.Campaign.Id), Ct));
		var wire = Wire(moved);

		// The directive beside the id, not the one it left: a stale nav here would pin the old owner on every client.
		Assert.Equal(hierarchy.Campaign.Id, wire["directiveId"]?.GetValue<string>());
		Assert.Equal(hierarchy.Campaign.Id, wire["directive"]?["id"]?.GetValue<string>());
	}

	[Fact]
	public async Task Editing_and_reparenting_a_sub_directive_returns_a_serializable_graph()
	{
		var hierarchy = await SeedAsync();

		var renamed = await Directives(api => api.UpdateStellarAsync(hierarchy.Strike.Id, new StellarDirectiveUpdate(Title: "Strike, revised"), Ct));
		Assert.Equal("Strike, revised", Wire<Directive>(renamed)["title"]?.GetValue<string>());

		var lifted = await Directives(api => api.UpdateStellarAsync(hierarchy.Strike.Id, new StellarDirectiveUpdate(ParentDirectiveId: new Optional<string?>(null)), Ct));
		Assert.Null(Wire<Directive>(lifted)["parentDirectiveId"]?.GetValue<string>());

		var nested = await Directives(api => api.UpdateStellarAsync(hierarchy.Strike.Id, new StellarDirectiveUpdate(ParentDirectiveId: hierarchy.Campaign.Id), Ct));
		Assert.Equal(hierarchy.Campaign.Id, Wire<Directive>(nested)["parentDirectiveId"]?.GetValue<string>());

		var parent = await Directives(api => api.UpdateStellarAsync(hierarchy.Campaign.Id, new StellarDirectiveUpdate(Title: "Campaign, revised"), Ct));
		Assert.Equal("Campaign, revised", Wire<Directive>(parent)["title"]?.GetValue<string>());
	}

	[Fact]
	public async Task Every_read_of_the_hierarchy_serializes_with_each_incentive_s_directive()
	{
		var hierarchy = await SeedAsync();

		var objectives = Wire(await Objectives(api => api.ListAsync(Ct))).AsArray();
		Assert.All(objectives, objective => Assert.Equal(objective!["directiveId"]?.GetValue<string>(), objective["directive"]?["id"]?.GetValue<string>()));

		Wire(await Objectives(api => api.GetAsync(hierarchy.Landing.Id, Ct)));
		Wire(await Directives(api => api.ListAsync(cancellationToken: Ct)));
		Wire(await Directives(api => api.GetAsync(hierarchy.Strike.Id, Ct)));
		Wire(await Directives(api => api.GetAsync(hierarchy.Campaign.Id, Ct)));

		var sprint = Wire(await Vault.WithScopeAsync(services => services.GetRequiredService<IOnrushSprintApi>().GetAsync(cancellationToken: Ct)));
		Assert.All(sprint["objectives"]!.AsArray(), objective => Assert.NotNull(objective!["directive"]?["id"]));

		var decree = Wire(await Vault.WithScopeAsync(services => services.GetRequiredService<IDeclarativeApi>().GetDecreeAsync(hierarchy.Standup.Id, Ct)));
		Assert.Equal(hierarchy.Strike.Id, decree["directive"]?["id"]?.GetValue<string>());

		var polaris = Wire(await Vault.WithScopeAsync(services => services.GetRequiredService<IPolarisCycleApi>().GetAsync(cancellationToken: Ct)));
		Assert.Equal(hierarchy.Strike.Id, polaris["executives"]?[0]?["incentive"]?["directive"]?["id"]?.GetValue<string>());

		Wire(await Vault.WithScopeAsync(services => services.GetRequiredService<ISystemApi>().GetBriefingAsync(Ct)));
	}
}
