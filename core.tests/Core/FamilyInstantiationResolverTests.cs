using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Watcher;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// A polymorphic path-sync model anchors an abstract family; which concrete member a path composes to must be
/// driven by the file's identity, not hard-coded (REFACTOR Alpha phase 3). This pins the directive family:
/// an <c>A…</c> identity selects the stellar member, a <c>LUNA…</c> identity the lunar member, and an absent or
/// unresolvable identity falls back to the family's declared default — so lunar and stellar are symmetric and a
/// future policy change is an attribute swap with no strings attached.
/// </summary>
public sealed class FamilyInstantiationResolverTests : VaultTestBase
{
	[Fact]
	public async Task Directive_family_resolves_instantiation_type_from_identity()
	{
		var stellar = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("Campaign", cancellationToken: TestContext.Current.CancellationToken));
		var lunar = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateLunarAsync("Sleep Law", cancellationToken: TestContext.Current.CancellationToken));

		await Vault.WithScopeAsync(services =>
		{
			var resolver = services.GetRequiredService<VaultFamilyInstantiationResolver>();
			var directiveModel = services.GetRequiredService<VaultPathSyncModelCatalog>()
				.GetModels()
				.Single(model => model.EntityType == typeof(Directive));

			Assert.Equal(typeof(StellarDirective), resolver.ResolveInstantiationType(directiveModel, stellar.Id));
			Assert.Equal(typeof(LunarDirective), resolver.ResolveInstantiationType(directiveModel, lunar.Id));

			// No identity yet (a brand-new, unidentified file) falls back to the declared default member, preserving
			// today's stellar default rather than guessing.
			Assert.Equal(typeof(StellarDirective), resolver.ResolveInstantiationType(directiveModel, null));
			Assert.Equal(typeof(StellarDirective), resolver.ResolveInstantiationType(directiveModel, "   "));

			return Task.CompletedTask;
		});
	}

	[Fact]
	public async Task Single_member_model_always_instantiates_its_own_type()
	{
		var objective = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("Campaign", cancellationToken: TestContext.Current.CancellationToken));

		await Vault.WithScopeAsync(services =>
		{
			var resolver = services.GetRequiredService<VaultFamilyInstantiationResolver>();
			var onrushModel = services.GetRequiredService<VaultPathSyncModelCatalog>()
				.GetModels()
				.Single(model => model.EntityType == typeof(OnrushSprint));

			// A non-family model has no members to select between: any identity resolves to its own type.
			Assert.Equal(typeof(OnrushSprint), resolver.ResolveInstantiationType(onrushModel, objective.Id));
			Assert.Equal(typeof(OnrushSprint), resolver.ResolveInstantiationType(onrushModel, null));

			return Task.CompletedTask;
		});
	}
}
