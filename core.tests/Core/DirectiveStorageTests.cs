using Microsoft.Extensions.DependencyInjection;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Stellar and lunar directives store under their own vault locations (PEP100): stellar under the Directives
/// root, lunar under the dedicated Moonlight root, with nesting composed beneath the parent directory.
/// </summary>
public sealed class DirectiveStorageTests : VaultTestBase
{
	[Fact]
	public async Task Stellar_directive_file_lands_under_directives_root()
	{
		var stellar = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("Campaign", cancellationToken: TestContext.Current.CancellationToken));

		var path = Vault.AbsolutePath("Directives/Campaign/Campaign.md");
		Assert.True(File.Exists(path));

		var content = File.ReadAllText(path);
		Assert.Contains($"puck: {stellar.Id}", content);
		Assert.Contains("status: Planned", content);
	}

	[Fact]
	public async Task Lunar_directive_file_lands_under_moonlight_root()
	{
		Assert.True(Directory.Exists(Vault.Layout.MoonlightRoot));

		var lunar = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateLunarAsync("Sleep Law", cancellationToken: TestContext.Current.CancellationToken));

		var path = Vault.AbsolutePath("Moonlight/Sleep Law/Sleep Law.md");
		Assert.True(File.Exists(path));

		var content = File.ReadAllText(path);
		Assert.Contains($"puck: {lunar.Id}", content);
		Assert.Contains("status: OnHold", content);

		// The stellar location must not receive the lunar file.
		Assert.False(Vault.VaultFileExists("Directives/Sleep Law/Sleep Law.md"));
	}

	[Fact]
	public async Task Nested_stellar_directive_lands_inside_parent_directory()
	{
		var parent = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("Campaign", cancellationToken: TestContext.Current.CancellationToken));
		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateFromParentAsync(parent.Id, "Strike", cancellationToken: TestContext.Current.CancellationToken));

		Assert.True(Vault.VaultFileExists("Directives/Campaign/Strike/Strike.md"));
	}

	[Fact]
	public async Task Lunar_update_rewrites_frontmatter_but_preserves_body()
	{
		var lunar = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateLunarAsync("Sleep Law", cancellationToken: TestContext.Current.CancellationToken));

		var path = Vault.AbsolutePath("Moonlight/Sleep Law/Sleep Law.md");
		var withBody = File.ReadAllText(path).TrimEnd() + Environment.NewLine + "Everglow body." + Environment.NewLine;
		File.WriteAllText(path, withBody);

		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.UpdateLunarAsync(lunar.Id, new LunarDirectiveUpdate(Codename: "MOON"), TestContext.Current.CancellationToken));

		var after = File.ReadAllText(path);
		Assert.Contains("Everglow body.", after);
		Assert.Contains("codename: MOON", after);
	}
}
