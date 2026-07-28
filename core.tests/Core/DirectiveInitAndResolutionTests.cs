using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Puck;
using Pleiades.Tests.Harness;
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

		// Known gap (pre-dates the sibling split): entity->note association for directives enumerates through the
		// path-sync candidate predicate, which deliberately excludes directives from watcher scanning — so no
		// associated note resolves. Slated for the registry/shape-strategy refactor.
		Assert.Null(stellarExistence.AssociatedNote);

		var lunarExistence = await Vault.WithScopeAsync(services => services
			.GetRequiredService<PuckEntityResolutionService>()
			.ResolveAsync(lunar.Id, TestContext.Current.CancellationToken));
		Assert.True(lunarExistence.Exists);
		Assert.Equal(nameof(LunarDirective), lunarExistence.EntityType);
		Assert.Equal("lunar-directive", lunarExistence.EntityKind);
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
