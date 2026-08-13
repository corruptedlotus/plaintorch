using Pleiades.Orchestration;
using Pleiades.Saga;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Markdown;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Golden path-composition suite (Phase 2 test gate): canonical path composition honors PUCK storage form,
/// storage shape, partitioning, and parent hierarchy across every file-backed entity. These pin the current
/// behaviour of <see cref="MarkdownFileLocator"/> before the shape-strategy refactor and must continue to hold
/// after it — the refactor is behaviour-preserving.
/// </summary>
public sealed class FileLocatorTests : VaultTestBase
{
	private MarkdownFileLocator Locator => Vault.GetSingleton<MarkdownFileLocator>();

	/// <summary>Vault-relative path with forward slashes, for stable golden assertions across platforms.</summary>
	private string Relative(string absolutePath)
	{
		return Path.GetRelativePath(Vault.VaultRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
	}

	// --- SingleFile shape, Quiet PUCK (title-only filename): objective / fate / decree ---

	[Fact]
	public void Quiet_objective_uses_title_only_filename()
	{
		var objective = new Objective { Id = "j12345678", Title = "Ship it" };

		var path = Locator.GetObjectiveFilePath(objective);

		Assert.Equal("Objectives/Ship it.md", Relative(path));
	}

	[Fact]
	public void Directive_owned_objective_lands_in_partition_folder()
	{
		var directive = new StellarDirective { Id = "A123", Title = "Campaign" };
		var objective = new Objective { Id = "j12345678", Title = "Ship it", DirectiveId = "A123" };

		var path = Locator.GetObjectiveFilePath(objective, directive);

		Assert.Equal("Directives/Campaign/Objectives/Ship it.md", Relative(path));
	}

	[Fact]
	public void Standalone_fate_lands_under_fates_root()
	{
		var fate = new Fate { Id = "e12345678", Title = "Destiny" };

		var path = Locator.GetFilePath(fate);

		Assert.Equal("Fates/Destiny.md", Relative(path));
	}

	[Fact]
	public void Directive_owned_fate_lands_in_fates_partition()
	{
		var directive = new StellarDirective { Id = "A123", Title = "Campaign" };
		var fate = new Fate { Id = "e12345678", Title = "Destiny", DirectiveId = "A123" };

		var path = Locator.GetFilePath(fate, directive);

		Assert.Equal("Directives/Campaign/Fates/Destiny.md", Relative(path));
	}

	[Fact]
	public void Standalone_decree_lands_under_decrees_root()
	{
		var decree = new Decree { Id = "r12345678", Title = "Rule" };

		var path = Locator.GetFilePath(decree);

		Assert.Equal("Decrees/Rule.md", Relative(path));
	}

	[Fact]
	public void Directive_owned_decree_lands_in_decrees_partition()
	{
		var directive = new StellarDirective { Id = "A123", Title = "Campaign" };
		var decree = new Decree { Id = "r12345678", Title = "Rule", DirectiveId = "A123" };

		var path = Locator.GetFilePath(decree, directive);

		Assert.Equal("Directives/Campaign/Decrees/Rule.md", Relative(path));
	}

	// --- SelfNamedDirectory shape, Quiet PUCK (title-only folder): directive family ---

	[Fact]
	public void Stellar_directive_root_is_self_named_under_directives()
	{
		var directive = new StellarDirective { Id = "A12345678", Title = "Campaign" };

		var path = Locator.GetDirectiveFilePath(directive);

		Assert.Equal("Directives/Campaign/Campaign.md", Relative(path));
	}

	[Fact]
	public void Lunar_directive_root_is_self_named_under_moonlight()
	{
		var directive = new LunarDirective { Id = "LUNA123", Title = "Sleep Law" };

		var path = Locator.GetDirectiveFilePath(directive);

		Assert.Equal("Moonlight/Sleep Law/Sleep Law.md", Relative(path));
	}

	[Fact]
	public void Nested_stellar_directive_composes_inside_parent_directory()
	{
		var parent = new StellarDirective { Id = "A11111111", Title = "Campaign" };
		var child = new StellarDirective { Id = "A22222222", Title = "Strike", ParentDirectiveId = parent.Id };

		var path = Locator.GetDirectiveFilePath(child, parent);

		Assert.Equal("Directives/Campaign/Strike/Strike.md", Relative(path));
	}

	// --- SelfNamedDirectory shape, Index PUCK (id-embedded folder): onrush sprint ---

	[Fact]
	public void Onrush_sprint_is_self_named_with_index_identity()
	{
		var sprint = new OnrushSprint { Id = "x0100", Title = "Launch" };

		var path = Locator.GetOnrushSprintFilePath(sprint);

		Assert.Equal("Onrush/x0100 - Launch/x0100 - Launch.md", Relative(path));
	}

	// --- SingleFile shape, Index PUCK, partitioned under parent self-named directory: executive order ---

	[Fact]
	public void Executive_order_composes_inside_owning_sprint_partition()
	{
		var sprint = new OnrushSprint { Id = "x0100", Title = "Launch" };
		var order = new ExecutiveOrder { Id = "x100-o01", Title = "Freeze", OnrushSprintId = sprint.Id };

		var path = Locator.GetExecutiveOrderFilePath(order, sprint);

		Assert.Equal("Onrush/x0100 - Launch/ExecutiveOrders/x100-o01 - Freeze.md", Relative(path));
	}

	[Fact]
	public void Executive_order_without_owning_sprint_throws()
	{
		var order = new ExecutiveOrder { Id = "x100-o01", Title = "Freeze", OnrushSprintId = "x0100" };

		Assert.Throws<InvalidOperationException>(() => Locator.GetExecutiveOrderFilePath(order, null));
	}

	// --- SingleFile shape, Index PUCK, no parent: polaris cycle ---

	[Fact]
	public void Index_polaris_cycle_embeds_id_in_filename()
	{
		var cycle = new PolarisCycle { Id = "20260101", Title = "New Year" };

		var path = Locator.GetPolarisCycleFilePath(cycle);

		Assert.Equal("Journal/20260101 - New Year.md", Relative(path));
	}

	// --- SelfNamedDirectory shape, Index PUCK, RelativePath override: lore page ---

	[Fact]
	public void Lore_page_falls_back_to_self_named_folder_under_saga()
	{
		var lorePage = new LorePage { Id = "Era1", Title = "Genesis" };

		var path = Locator.GetLorePageFilePath(lorePage);

		Assert.Equal("Saga/Era1 - Genesis/Era1 - Genesis.md", Relative(path));
	}

	[Fact]
	public void Lore_page_honors_its_relative_path_override()
	{
		var lorePage = new LorePage { Id = "Era1", Title = "Genesis", RelativePath = "Saga/Custom Place/Anchor.md" };

		var path = Locator.GetLorePageFilePath(lorePage);

		Assert.Equal("Saga/Custom Place/Anchor.md", Relative(path));
	}
}
