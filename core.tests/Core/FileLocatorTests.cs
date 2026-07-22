using Pleiades.Orchestration;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Markdown;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Canonical path composition honors PUCK storage form, partitioning, and parent hierarchy.
/// </summary>
public sealed class FileLocatorTests : VaultTestBase
{
	[Fact]
	public void Quiet_objective_uses_title_only_filename()
	{
		var locator = Vault.GetSingleton<MarkdownFileLocator>();
		var objective = new Objective { Id = "j12345678", Title = "Ship it" };

		var path = locator.GetObjectiveFilePath(objective);

		Assert.Equal("Ship it.md", Path.GetFileName(path));
		Assert.StartsWith(Vault.Layout.ObjectivesRoot, path);
	}

	[Fact]
	public void Index_polaris_cycle_embeds_id_in_filename()
	{
		var locator = Vault.GetSingleton<MarkdownFileLocator>();
		var cycle = new PolarisCycle { Id = "20260101", Title = "New Year" };

		var path = locator.GetPolarisCycleFilePath(cycle);

		Assert.Equal("20260101 - New Year.md", Path.GetFileName(path));
	}

	[Fact]
	public void Directive_owned_objective_lands_in_partition_folder()
	{
		var locator = Vault.GetSingleton<MarkdownFileLocator>();
		var directive = new StellarDirective { Id = "A123", Title = "Campaign" };
		var objective = new Objective { Id = "j12345678", Title = "Ship it", DirectiveId = "A123" };

		var path = locator.GetObjectiveFilePath(objective, directive);

		Assert.Equal("Ship it.md", Path.GetFileName(path));
		Assert.Equal("Objectives", Path.GetFileName(Path.GetDirectoryName(path)));
	}
}
