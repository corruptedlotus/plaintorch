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

	[Fact(Skip = "CONFIRMED BUG (fix belongs in REFACTOR Alpha phase 4). PathBoundVaultStorageModePolicyService.TryResolveWatchPath applies its self-named-directory fallback (<dir>/<dir>.md) to every path-bound mode regardless of shape, and to a model's own scan root: a directory event for the enforced ./Journal root resolves to a synthetic Journal/Journal.md as PolarisCycle (a SingleFile model). The fallback should be gated to SelfNamedDirectory shape and must exclude the scan root itself. The catalog's own TryResolveWatchPath already guards by shape; the engine/mode-policy path (what the live watcher uses) does not.")]
	public async Task Enforced_journal_root_directory_is_not_resolved_as_a_self_named_directive()
	{
		// A watcher directory event for the enforced ./Journal root must not resolve to a synthetic Journal/Journal.md
		// as if the root folder were a self-named entity directory.
		Vault.WriteVaultFile("Journal/Untitled note.md", "# Untitled note" + Environment.NewLine + "body" + Environment.NewLine);

		var (resolved, inspectPath, entityName) = await Vault.WithScopeAsync(services =>
		{
			var engine = services.GetRequiredService<VaultStoragePolicyEngine>();
			var didResolve = engine.TryResolveWatchPath(Vault.AbsolutePath("Journal"), out var inspect, out var model);
			return Task.FromResult((didResolve, inspect, model?.EntityName));
		});

		Assert.False(resolved, $"Journal root resolved to inspect path '{inspectPath}' as model '{entityName}'.");
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
