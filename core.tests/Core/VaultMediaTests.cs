using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Media;
using Pleiades.Vault.Policy;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Directive icons and banners (PEP105) store image files in an <c>_assets</c> folder, record a keyed reference
/// in frontmatter, and resolve into a <see cref="MediaReference"/> companion — self (<c>media:</c>) media beside
/// the directive, vault (<c>vault:</c>) media in the vault root — while the underscore folder stays out of
/// markdown discovery.
/// </summary>
public sealed class VaultMediaTests : VaultTestBase
{
	private static readonly string SampleImage = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A]);

	private Task<StellarDirective> CreateCampaignAsync()
		=> Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("Campaign", cancellationToken: TestContext.Current.CancellationToken));

	private Task<Directive> SetIconAsync(string directiveId, DirectiveIconRequest request)
		=> Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.SetIconAsync(directiveId, request, TestContext.Current.CancellationToken));

	private Task<Directive> SetBannerAsync(string directiveId, DirectiveBannerRequest request)
		=> Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.SetBannerAsync(directiveId, request, TestContext.Current.CancellationToken));

	[Fact]
	public async Task Uploading_an_icon_stores_self_media_and_resolves_the_companion()
	{
		var directive = await CreateCampaignAsync();

		var updated = await SetIconAsync(directive.Id, new DirectiveIconRequest(Upload: new MediaUpload("crest.png", SampleImage)));

		Assert.Equal("media:crest.png", updated.Icon);
		Assert.NotNull(updated.IconMedia);
		Assert.Equal("media:crest.png", updated.IconMedia.Key);
		Assert.Equal(MediaKind.Media, updated.IconMedia.Type);
		Assert.Equal("Directives/Campaign/_assets/crest.png", updated.IconMedia.Path);
		Assert.True(Vault.VaultFileExists("Directives/Campaign/_assets/crest.png"));

		// The reference round-trips through frontmatter (quoted because it contains a colon).
		var content = Vault.ReadVaultFile("Directives/Campaign/Campaign.md");
		Assert.Contains("media:crest.png", content);

		// A fresh read resolves the persisted reference back into the companion.
		var reloaded = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.GetAsync(directive.Id, TestContext.Current.CancellationToken));
		Assert.Equal(MediaKind.Media, reloaded!.IconMedia!.Type);
		Assert.Equal("Directives/Campaign/_assets/crest.png", reloaded.IconMedia.Path);
	}

	[Fact]
	public async Task Uploading_a_banner_stores_self_media_and_resolves_the_companion()
	{
		var directive = await CreateCampaignAsync();

		var updated = await SetBannerAsync(directive.Id, new DirectiveBannerRequest(Upload: new MediaUpload("hero.png", SampleImage)));

		Assert.Equal("media:hero.png", updated.Banner);
		Assert.NotNull(updated.BannerMedia);
		Assert.Equal(MediaKind.Media, updated.BannerMedia.Type);
		Assert.Equal("Directives/Campaign/_assets/hero.png", updated.BannerMedia.Path);
		Assert.True(Vault.VaultFileExists("Directives/Campaign/_assets/hero.png"));
	}

	[Fact]
	public async Task Uploading_to_vault_stores_shared_media_at_the_root()
	{
		var directive = await CreateCampaignAsync();

		var updated = await SetIconAsync(directive.Id, new DirectiveIconRequest(Upload: new MediaUpload("logo.png", SampleImage), Vault: true));

		Assert.Equal("vault:logo.png", updated.Icon);
		Assert.NotNull(updated.IconMedia);
		Assert.Equal(MediaKind.Vault, updated.IconMedia.Type);
		Assert.Equal("_assets/logo.png", updated.IconMedia.Path);
		Assert.True(Vault.VaultFileExists("_assets/logo.png"));
		// It is shared, not filed beside the directive.
		Assert.False(Vault.VaultFileExists("Directives/Campaign/_assets/logo.png"));
	}

	[Fact]
	public async Task Setting_a_glyph_reference_resolves_to_an_icon_kind_without_a_path()
	{
		var directive = await CreateCampaignAsync();

		var updated = await SetIconAsync(directive.Id, new DirectiveIconRequest(Reference: "lucide:star"));

		Assert.Equal("lucide:star", updated.Icon);
		Assert.NotNull(updated.IconMedia);
		Assert.Equal(MediaKind.Icon, updated.IconMedia.Type);
		Assert.Null(updated.IconMedia.Path);
		// A glyph is not a media asset, so nothing is written to the asset folder.
		Assert.False(Directory.Exists(Vault.AbsolutePath("Directives/Campaign/_assets")));
	}

	[Fact]
	public async Task Clearing_a_self_icon_archives_the_stored_image()
	{
		var directive = await CreateCampaignAsync();
		await SetIconAsync(directive.Id, new DirectiveIconRequest(Upload: new MediaUpload("crest.png", SampleImage)));
		Assert.True(Vault.VaultFileExists("Directives/Campaign/_assets/crest.png"));

		var cleared = await SetIconAsync(directive.Id, new DirectiveIconRequest(Clear: true));

		Assert.Null(cleared.Icon);
		Assert.Null(cleared.IconMedia);
		// The file is archived to the graveyard, not left in place and not hard-deleted.
		Assert.False(Vault.VaultFileExists("Directives/Campaign/_assets/crest.png"));
		var archived = await Vault.QueryAsync(context => context.FileGraveyardEntries
			.AnyAsync(entry => entry.Reason == "directive.icon-clear", TestContext.Current.CancellationToken));
		Assert.True(archived);
	}

	[Fact]
	public async Task Clearing_a_vault_reference_leaves_the_shared_file_in_place()
	{
		var directive = await CreateCampaignAsync();
		// A manually-placed shared asset referenced by two directives must survive one of them clearing it.
		Vault.WriteVaultFile("_assets/logo.png", "shared");
		await SetIconAsync(directive.Id, new DirectiveIconRequest(Reference: "vault:logo.png"));

		var cleared = await SetIconAsync(directive.Id, new DirectiveIconRequest(Clear: true));

		Assert.Null(cleared.Icon);
		Assert.True(Vault.VaultFileExists("_assets/logo.png"));
	}

	[Fact]
	public void Asset_folder_is_ignored_by_the_watcher_path_policy()
	{
		var assetPath = Vault.AbsolutePath("Directives/Campaign/_assets/crest.png");
		Assert.True(Vault.GetSingleton<VaultWatcherPathPolicy>().ShouldIgnorePath(assetPath));
	}
}
