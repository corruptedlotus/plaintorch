using Pleiades.Orchestration;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Markdown;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Reverse path composition (REFACTOR Alpha phase 2): given a canonical markdown path, the composer derives an
/// entity's loose identity and its parent relation from the declared parent policy. These pin the behaviour that
/// phase 4's consolidation of the (currently triplicated) containing-owner resolvers must preserve.
/// </summary>
public sealed class PathCompositionReverseTests : VaultTestBase
{
	private VaultStoragePathComposer Composer => Vault.GetSingleton<VaultStoragePathComposer>();

	private static string Frontmatter(string puck) => "---" + "\n" + $"puck: {puck}" + "\n" + "---" + "\n";

	[Fact]
	public void Nested_directive_resolves_its_parent_directive_from_the_containing_folder()
	{
		Vault.WriteVaultFile("Directives/Campaign/Campaign.md", Frontmatter("A11111111"));
		Vault.WriteVaultFile("Directives/Campaign/Strike/Strike.md", Frontmatter("A22222222"));

		var child = new StellarDirective { Id = string.Empty, Title = string.Empty };
		Composer.ApplyCompositionFromPath(child, Vault.AbsolutePath("Directives/Campaign/Strike/Strike.md"));

		Assert.Equal("Strike", child.Title);
		Assert.Equal("A11111111", child.ParentDirectiveId);
	}

	[Fact]
	public void Executive_order_resolves_id_title_and_owning_sprint_from_the_partition_path()
	{
		Vault.WriteVaultFile("Onrush/x0100 - Launch/x0100 - Launch.md", Frontmatter("x0100"));
		Vault.WriteVaultFile("Onrush/x0100 - Launch/ExecutiveOrders/x100-o01 - Freeze.md", string.Empty);

		var order = new ExecutiveOrder { Id = string.Empty, Title = string.Empty, OnrushSprintId = string.Empty };
		Composer.ApplyCompositionFromPath(order, Vault.AbsolutePath("Onrush/x0100 - Launch/ExecutiveOrders/x100-o01 - Freeze.md"));

		Assert.Equal("x100-o01", order.Id);
		Assert.Equal("Freeze", order.Title);
		Assert.Equal("x0100", order.OnrushSprintId);
	}

	[Fact]
	public void Standalone_polaris_cycle_derives_index_identity_and_no_parent()
	{
		var cycle = new PolarisCycle { Id = string.Empty, Title = string.Empty };
		Composer.ApplyCompositionFromPath(cycle, Vault.AbsolutePath("Journal/20260101 - New Year.md"));

		Assert.Equal("20260101", cycle.Id);
		Assert.Equal("New Year", cycle.Title);
	}
}
