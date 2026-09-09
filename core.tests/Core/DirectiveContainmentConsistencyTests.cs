using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Policy;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// "Which directive contains this path?" is now answered by exactly one ownership-boundary-aware resolver
/// (<see cref="VaultWatcherPathPolicy.TryResolveContainingDirectiveId"/>); the path composer and the path-sync catalog
/// route through it rather than each carrying their own walk-up (which could — and did — drift). These pin the single
/// resolver so a future divergent copy would break: the composer's path-derived parent must equal the policy's answer,
/// which must equal the real directive, at any depth and for a dash-named directive.
/// </summary>
public sealed class DirectiveContainmentConsistencyTests : VaultTestBase
{
	private Task<StellarDirective> CreateDirectiveAsync(string title)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync(title, cancellationToken: TestContext.Current.CancellationToken));

	private static string? ComposerResolvedDirectiveId(TestVault vault, string absolutePath)
	{
		var composer = vault.GetSingleton<VaultStoragePathComposer>();
		var objective = new Objective { Id = string.Empty, Title = string.Empty };
		composer.ApplyCompositionFromPath(objective, absolutePath);
		return objective.DirectiveId;
	}

	[Fact]
	public async Task The_composer_and_the_path_policy_resolve_the_same_directive_deep_in_a_partition()
	{
		var directive = await CreateDirectiveAsync("Campaign");
		Vault.WriteVaultFile("Directives/Campaign/Objectives/Backlog/Task.md", "# Task" + Environment.NewLine);
		var path = Vault.AbsolutePath("Directives/Campaign/Objectives/Backlog/Task.md");

		var composerId = ComposerResolvedDirectiveId(Vault, path);
		var policyId = Vault.GetSingleton<VaultWatcherPathPolicy>().TryResolveContainingDirectiveId(path);

		Assert.Equal(directive.Id, policyId);
		Assert.Equal(directive.Id, composerId);
	}

	[Fact]
	public async Task The_composer_and_the_path_policy_agree_for_a_dash_named_directive()
	{
		var directive = await CreateDirectiveAsync("2024 - Roadmap");
		Vault.WriteVaultFile("Directives/2024 - Roadmap/Note.md", "# Note" + Environment.NewLine);
		var path = Vault.AbsolutePath("Directives/2024 - Roadmap/Note.md");

		var composerId = ComposerResolvedDirectiveId(Vault, path);
		var policyId = Vault.GetSingleton<VaultWatcherPathPolicy>().TryResolveContainingDirectiveId(path);

		Assert.Equal(directive.Id, policyId);
		Assert.Equal(directive.Id, composerId);
	}
}
