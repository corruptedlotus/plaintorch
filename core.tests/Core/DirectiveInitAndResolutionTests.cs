using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Puck;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Watcher;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The directive family anchors on an abstract base: freeform init must materialize the concrete stellar
/// sibling, and PUCK resolution must land each id declaration on its concrete kind (PEP100).
/// </summary>
public sealed class DirectiveInitAndResolutionTests : VaultTestBase
{
	[Fact]
	public async Task Init_from_path_materializes_a_stellar_directive()
	{
		Vault.WriteVaultFile("Directives/Campaign/Campaign.md", "# Campaign" + Environment.NewLine + "Freeform authored body." + Environment.NewLine);

		var created = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.InitializeFromPathAsync("Directives/Campaign/Campaign.md", TestContext.Current.CancellationToken));

		var stellar = Assert.IsType<StellarDirective>(created);
		Assert.StartsWith("A", stellar.Id);
		Assert.Equal(9, stellar.Id.Length); // A{S:8}: concrete stellar identity, not the base's legacy A{S:6}.
		Assert.Equal("Campaign", stellar.Title);

		var persisted = await Vault.QueryAsync(context => context.Directives
			.AsNoTracking()
			.OfType<StellarDirective>()
			.SingleAsync(item => item.Id == stellar.Id, TestContext.Current.CancellationToken));
		Assert.Equal(DirectiveStatus.Planned, persisted.Status);

		var content = Vault.ReadVaultFile("Directives/Campaign/Campaign.md");
		Assert.Contains($"puck: {stellar.Id}", content);
		Assert.Contains("Freeform authored body.", content);
	}

	[Fact]
	public async Task Init_path_composes_a_lunar_identity_to_the_lunar_member()
	{
		// Author a lunar directive so its on-disk file carries a real LUNA identity in frontmatter.
		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateLunarAsync("Sleep Law", cancellationToken: TestContext.Current.CancellationToken));

		// Re-inspecting that file through the directive init path must path-compose to the lunar member: the file's
		// own identity selects it, where before this refactor the family collapsed to its stellar fallback regardless
		// of identity (REFACTOR Alpha phase 3: identity-driven concrete-type resolution).
		var absolutePath = Path.Combine(Vault.VaultRoot, "Moonlight", "Sleep Law", "Sleep Law.md");
		var candidate = await Vault.WithScopeAsync(services => services
			.GetRequiredService<VaultMarkdownDiscoveryService>()
			.InspectDirectiveInitPathAsync(absolutePath, "test", TestContext.Current.CancellationToken));

		Assert.NotNull(candidate);
		Assert.IsType<LunarDirective>(candidate!.ParsedModel);
	}

	[Fact]
	public async Task Stellar_and_lunar_ids_resolve_to_their_concrete_kinds()
	{
		var stellar = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("Campaign", cancellationToken: TestContext.Current.CancellationToken));
		var lunar = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateLunarAsync("Sleep Law", cancellationToken: TestContext.Current.CancellationToken));

		var stellarExistence = await Vault.WithScopeAsync(services => services
			.GetRequiredService<PuckEntityResolutionService>()
			.ResolveAsync(stellar.Id, TestContext.Current.CancellationToken));
		Assert.True(stellarExistence.Exists);
		Assert.Equal(nameof(StellarDirective), stellarExistence.EntityType);
		Assert.Equal("stellar-directive", stellarExistence.EntityKind);

		// Freeform directive note association now resolves (REFACTOR Alpha phase 2): the file is located by
		// enumerating self-named markdown and matched by its frontmatter PUCK, not by path shape — an
		// identity-driven, non-path-composition interaction.
		Assert.Equal("Directives/Campaign/Campaign.md", stellarExistence.AssociatedNote);

		var lunarExistence = await Vault.WithScopeAsync(services => services
			.GetRequiredService<PuckEntityResolutionService>()
			.ResolveAsync(lunar.Id, TestContext.Current.CancellationToken));
		Assert.True(lunarExistence.Exists);
		Assert.Equal(nameof(LunarDirective), lunarExistence.EntityType);
		Assert.Equal("lunar-directive", lunarExistence.EntityKind);

		// The family-anchored directive model resolves lunar notes under Moonlight too.
		Assert.Equal("Moonlight/Sleep Law/Sleep Law.md", lunarExistence.AssociatedNote);
	}

	[Fact]
	public async Task Legacy_six_char_ids_resolve_through_the_base_declaration_to_stellar_rows()
	{
		// Pre-split vaults hold stellar rows under the base A{S:6} declaration; the migration converts their
		// discriminator but their ids keep resolving through the base declaration.
		await Vault.WithScopeAsync(async services =>
		{
			var context = services.GetRequiredService<Pleiades.Vault.Database.PlainfraContext>();
			context.Directives.Add(new StellarDirective { Id = "A123456", Title = "Legacy" });
			await context.SaveChangesAsync(TestContext.Current.CancellationToken);
		});

		var existence = await Vault.WithScopeAsync(services => services
			.GetRequiredService<PuckEntityResolutionService>()
			.ResolveAsync("A123456", TestContext.Current.CancellationToken));

		Assert.True(existence.Exists);
		Assert.Equal("directive", existence.EntityKind);
		Assert.IsType<StellarDirective>(existence.Entity);
	}
}
