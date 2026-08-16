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
/// Directive icons and banners (PEP105) resolve into a <see cref="MediaReference"/> companion — self
/// (<c>media:</c>) media beside the directive, vault (<c>vault:</c>) media in the vault root — while the underscore
/// folder stays out of markdown discovery. Storing the image and selecting it onto a field are separate steps: the
/// media domain (<see cref="IMediaApi"/>) stores the bytes and returns a key, and the directive only ever
/// references that key.
/// </summary>
public sealed class VaultMediaTests : VaultTestBase
{
	private static readonly string SampleImage = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A]);

	private Task<StellarDirective> CreateCampaignAsync()
		=> Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("Campaign", cancellationToken: TestContext.Current.CancellationToken));

	private Task<MediaStoreResult> UploadEntityAsync(string entityType, string entityId, MediaUpload upload)
		=> Vault.WithScopeAsync(services => services
			.GetRequiredService<IMediaApi>()
			.UploadEntityAsync(entityType, entityId, upload, TestContext.Current.CancellationToken));

	private Task<MediaStoreResult> UploadVaultAsync(MediaUpload upload)
		=> Vault.WithScopeAsync(services => services
			.GetRequiredService<IMediaApi>()
			.UploadVaultAsync(upload, TestContext.Current.CancellationToken));

	private Task<Directive> SetIconAsync(string directiveId, DirectiveIconRequest request)
		=> Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.SetIconAsync(directiveId, request, TestContext.Current.CancellationToken));

	private Task<Directive> SetBannerAsync(string directiveId, DirectiveBannerRequest request)
		=> Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.SetBannerAsync(directiveId, request, TestContext.Current.CancellationToken));

	[Fact]
	public async Task Uploading_an_icon_to_the_entity_then_referencing_it_resolves_the_self_companion()
	{
		var directive = await CreateCampaignAsync();

		// Step one — the media domain stores the image beside the directive and hands back its key.
		var stored = await UploadEntityAsync("directive", directive.Id, new MediaUpload("crest.png", SampleImage));
		Assert.Equal("media:crest.png", stored.Key);
		Assert.True(Vault.VaultFileExists("Directives/Campaign/_assets/crest.png"));

		// Step two — the directive references the stored key; it never stored anything itself.
		var updated = await SetIconAsync(directive.Id, new DirectiveIconRequest(Reference: stored.Key));

		Assert.Equal("media:crest.png", updated.Icon);
		Assert.NotNull(updated.IconMedia);
		Assert.Equal("media:crest.png", updated.IconMedia.Key);
		Assert.Equal(MediaKind.Media, updated.IconMedia.Type);
		Assert.Equal("Directives/Campaign/_assets/crest.png", updated.IconMedia.Path);

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
	public async Task Uploading_a_banner_to_the_entity_then_referencing_it_resolves_the_self_companion()
	{
		var directive = await CreateCampaignAsync();

		var stored = await UploadEntityAsync("directive", directive.Id, new MediaUpload("hero.png", SampleImage));
		var updated = await SetBannerAsync(directive.Id, new DirectiveBannerRequest(Reference: stored.Key));

		Assert.Equal("media:hero.png", updated.Banner);
		Assert.NotNull(updated.BannerMedia);
		Assert.Equal(MediaKind.Media, updated.BannerMedia.Type);
		Assert.Equal("Directives/Campaign/_assets/hero.png", updated.BannerMedia.Path);
		Assert.True(Vault.VaultFileExists("Directives/Campaign/_assets/hero.png"));
	}

	[Fact]
	public async Task Uploading_to_the_vault_then_referencing_it_resolves_the_shared_companion()
	{
		var directive = await CreateCampaignAsync();

		var stored = await UploadVaultAsync(new MediaUpload("logo.png", SampleImage));
		Assert.Equal("vault:logo.png", stored.Key);
		var updated = await SetIconAsync(directive.Id, new DirectiveIconRequest(Reference: stored.Key));

		Assert.Equal("vault:logo.png", updated.Icon);
		Assert.NotNull(updated.IconMedia);
		Assert.Equal(MediaKind.Vault, updated.IconMedia.Type);
		Assert.Equal("_assets/logo.png", updated.IconMedia.Path);
		Assert.True(Vault.VaultFileExists("_assets/logo.png"));
		// It is shared, not filed beside the directive.
		Assert.False(Vault.VaultFileExists("Directives/Campaign/_assets/logo.png"));
	}

	[Fact]
	public async Task The_media_domain_lists_a_stored_vault_asset()
	{
		await UploadVaultAsync(new MediaUpload("logo.png", SampleImage));

		var listed = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IMediaApi>()
			.ListVaultAssetsAsync(TestContext.Current.CancellationToken));

		Assert.Contains("logo.png", listed);
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
		var stored = await UploadEntityAsync("directive", directive.Id, new MediaUpload("crest.png", SampleImage));
		await SetIconAsync(directive.Id, new DirectiveIconRequest(Reference: stored.Key));
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
