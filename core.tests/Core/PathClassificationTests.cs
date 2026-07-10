using Microsoft.Extensions.DependencyInjection;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Policy;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Path classification: objective-shaped paths resolve to Objective; directive self-named files do not.
/// </summary>
public sealed class PathClassificationTests : VaultTestBase
{
	[Fact]
	public async Task Objective_file_under_objectives_root_classifies_as_objective()
	{
		Vault.WriteVaultFile("Objectives/Ship it.md", Frontmatter("j00000001"));

		var entity = await ResolveEntityNameAsync(Vault.AbsolutePath("Objectives/Ship it.md"));

		Assert.Equal("Objective", entity);
	}

	[Fact]
	public async Task Directive_self_named_file_does_not_classify_as_objective()
	{
		Vault.WriteVaultFile("Directives/A1 - Foo/A1 - Foo.md", Frontmatter("A1"));

		var entity = await ResolveEntityNameAsync(Vault.AbsolutePath("Directives/A1 - Foo/A1 - Foo.md"));

		Assert.NotEqual("Objective", entity);
		Assert.Equal("Directive", entity);
	}

	private Task<string?> ResolveEntityNameAsync(string absolutePath)
	{
		return Vault.WithScopeAsync(services =>
		{
			var engine = services.GetRequiredService<VaultStoragePolicyEngine>();
			engine.TryResolveWatchPath(absolutePath, out _, out var model);
			return Task.FromResult(model?.EntityName);
		});
	}

	private static string Frontmatter(string puck)
	{
		return "---" + Environment.NewLine + $"puck: {puck}" + Environment.NewLine + "---" + Environment.NewLine;
	}
}
