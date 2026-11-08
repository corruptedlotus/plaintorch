using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Hosting;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The navigations the model auto-includes stay auto-included, and an objective is read with its owning directive.
/// <para>
/// <c>Incentive.Directive</c> silently stopped being eager-loaded when the declarative hierarchy gained its abstract
/// base: the first <c>Entity&lt;Declarative&gt;()</c> call rebuilt the inherited navigation and dropped the flag set
/// just above it. From then on every objective read — the listing, a single get, an onrush's members — carried
/// <c>directive: null</c> beside its <c>directiveId</c>, and only a write response (a tracked entity, fixed up) carried
/// the directive. So the clients showed an objective owned by a directive as a world quest until it was next edited.
/// </para>
/// </summary>
public sealed class ModelAutoIncludeTests : VaultTestBase
{
	private static readonly JsonSerializerOptions HostOptions = CreateHostOptions();

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private static JsonSerializerOptions CreateHostOptions()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		PlaintorchHostFactory.ConfigureSerializer(options);
		return options;
	}

	[Theory]
	[InlineData(typeof(Incentive), nameof(Incentive.Directive))]
	[InlineData(typeof(Attentive), nameof(Attentive.AffinityTimeframe))]
	[InlineData(typeof(Executive), nameof(Executive.Incentive))]
	[InlineData(typeof(Executive), nameof(Executive.AffinityTimeframe))]
	public Task A_configured_auto_include_is_eager_loaded_in_the_built_model(Type entityType, string navigationName)
	{
		return Vault.WithScopeAsync(services =>
		{
			var model = services.GetRequiredService<PlainfraContext>().Model;
			var navigation = model.FindEntityType(entityType)?.FindNavigation(navigationName);
			Assert.NotNull(navigation);
			Assert.True(navigation.IsEagerLoaded, $"{entityType.Name}.{navigationName} is no longer auto-included.");
			return Task.CompletedTask;
		});
	}

	[Fact]
	public async Task An_objective_is_read_with_its_directive_wherever_it_is_listed()
	{
		var directive = await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>().CreateStandaloneAsync("Mars", cancellationToken: Ct));
		var sprint = await Vault.WithScopeAsync(services => services.GetRequiredService<IOnrushSprintApi>().StartNewAsync(cancellationToken: Ct));
		var member = await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>().CreateFromDirectiveAsync(directive.Id, "Land", sprint.Id, cancellationToken: Ct));
		var sibling = await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>().CreateFromDirectiveAsync(directive.Id, "Launch", cancellationToken: Ct));

		var listed = await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>().ListAsync(Ct));
		var fetched = await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>().GetAsync(sibling.Id, Ct));
		var current = await Vault.WithScopeAsync(services => services.GetRequiredService<IOnrushSprintApi>().GetAsync(cancellationToken: Ct));

		Assert.All(listed, objective => Assert.Equal(directive.Id, objective.Directive?.Id));
		Assert.Equal(directive.Id, fetched?.Directive?.Id);
		Assert.NotNull(current);
		Assert.Equal(directive.Id, Assert.Single(current.Objectives, objective => objective.Id == member.Id).Directive?.Id);

		// And on the wire, where the clients read it: the directive itself beside its id, not a null the SDK must
		// take for "not loaded".
		foreach (var node in Assert.IsType<JsonArray>(JsonNode.Parse(JsonSerializer.Serialize(listed, HostOptions))))
		{
			var objective = Assert.IsType<JsonObject>(node);
			Assert.Equal(directive.Id, objective["directiveId"]?.GetValue<string>());
			Assert.Equal(directive.Id, objective["directive"]?["id"]?.GetValue<string>());
		}
	}
}
