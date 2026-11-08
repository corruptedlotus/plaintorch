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
/// The system briefing composes its onrush sprint, Polaris cycle and active lore pages from the services that own them,
/// so what a briefing card reads is exactly what that entity's own endpoint serves.
/// <para>
/// The Onrush card walks its dependency groups from the sprint's members, its checkpoints among them. The briefing once
/// loaded the sprint on its own, without its checkpoints, and every prerequisite of the milestone went missing from the
/// card — while the dependency canvas, reading the sprint's own endpoint, showed them.
/// </para>
/// </summary>
public sealed class SystemBriefingCompositionTests : VaultTestBase
{
	private static readonly JsonSerializerOptions HostOptions = CreateHostOptions();

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private static JsonSerializerOptions CreateHostOptions()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		PlaintorchHostFactory.ConfigureSerializer(options);
		return options;
	}

	/// <summary>Serializes as an endpoint returning <typeparamref name="T"/> would.</summary>
	private static JsonNode? Wire<T>(T value) => JsonNode.Parse(JsonSerializer.Serialize(value, HostOptions));

	private Task<T> Onrush<T>(Func<IOnrushSprintApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IOnrushSprintApi>()));
	private Task<T> Polaris<T>(Func<IPolarisCycleApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IPolarisCycleApi>()));
	private Task<T> Objectives<T>(Func<IObjectiveApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IObjectiveApi>()));
	private Task<T> Lore<T>(Func<ILorePageApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<ILorePageApi>()));
	private Task<SystemBriefing> BriefingAsync() => Vault.WithScopeAsync(services => services.GetRequiredService<ISystemApi>().GetBriefingAsync(Ct));

	private static IEnumerable<string> CheckpointIds(JsonNode? sprint)
		=> sprint?["checkpoints"]?.AsArray().Select(checkpoint => checkpoint!["id"]!.GetValue<string>()) ?? [];

	[Fact]
	public async Task The_active_onrush_on_the_briefing_carries_its_milestone()
	{
		var sprint = await Onrush(api => api.StartNewAsync(cancellationToken: Ct));

		var briefing = await BriefingAsync();

		Assert.Equal("active", briefing.OnrushSelectionMode);
		Assert.Contains(sprint.MilestoneCheckpointId, CheckpointIds(Wire(briefing)?["currentOnrush"]));
	}

	[Fact]
	public async Task The_onrush_in_planning_on_the_briefing_carries_its_milestone()
	{
		var sprint = await Onrush(api => api.PlanAsync(new OnrushSprintPlan("Next Onrush"), Ct));

		var briefing = await BriefingAsync();

		Assert.Equal("planning", briefing.OnrushSelectionMode);
		Assert.Contains(sprint.MilestoneCheckpointId, CheckpointIds(Wire(briefing)?["currentOnrush"]));
	}

	[Fact]
	public async Task Without_an_onrush_the_briefing_names_no_selection()
	{
		var briefing = await BriefingAsync();

		Assert.Null(briefing.OnrushSelectionMode);
		Assert.Null(briefing.CurrentOnrush);
	}

	[Fact]
	public async Task The_briefing_serves_its_sprint_cycle_and_lore_as_their_own_services_do()
	{
		var sprint = await Onrush(api => api.StartNewAsync(cancellationToken: Ct));
		var member = await Objectives(api => api.CreateStandaloneAsync("Member", cancellationToken: Ct));
		await Objectives(api => api.AddToOnrushAsync(member.Id, sprint.Id, Ct));
		await Polaris(api => api.StartNewAsync(cancellationToken: Ct));
		await Polaris(api => api.PlanExecutiveAsync(new PolarisExecutivePlan(PolarisExecutivePlanningMode.FromObjective, ObjectiveId: member.Id, Estimation: 60), cancellationToken: Ct));
		var era = await Lore(api => api.CreateAsync(new LorePageCreateRequest(null, "First Era", new DateOnly(2020, 1, 1)), Ct));
		await Lore(api => api.CreateAsync(new LorePageCreateRequest(era.Id, "First Chapter", new DateOnly(2020, 2, 1)), Ct));

		var briefing = Wire(await BriefingAsync());
		var ownSprint = Wire(await Onrush(api => api.GetAsync(cancellationToken: Ct)));
		var ownCycle = Wire(await Polaris(api => api.GetAsync(cancellationToken: Ct)));
		var ownLore = Wire(await Lore(api => api.ListActiveAsync(Ct)));

		Assert.NotNull(ownCycle);
		Assert.Equal(2, ownLore!.AsArray().Count);
		Assert.True(JsonNode.DeepEquals(ownSprint, briefing!["currentOnrush"]), $"Briefing sprint differs from its own endpoint's:\n{briefing["currentOnrush"]}\n---\n{ownSprint}");
		Assert.True(JsonNode.DeepEquals(ownCycle, briefing["currentPolaris"]), $"Briefing cycle differs from its own endpoint's:\n{briefing["currentPolaris"]}\n---\n{ownCycle}");
		Assert.True(JsonNode.DeepEquals(ownLore, briefing["activeLorePages"]), $"Briefing lore differs from its own service's:\n{briefing["activeLorePages"]}\n---\n{ownLore}");
	}
}
