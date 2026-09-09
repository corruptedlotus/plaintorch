using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Markdown;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// A directive keeps its PUCK in frontmatter (Quiet), so a directive whose title contains the " - " separator
/// (e.g. "2024 - Roadmap") must still be resolved by its real identity. The containing-directive resolvers used to
/// loose-parse the folder name and read the dashed title's prefix ("2024") as a phantom PUCK, falling back to
/// frontmatter only when that prefix was empty — so a child inside such a directive resolved to a non-existent parent
/// and was silently orphaned. The resolvers now read the frontmatter identity authoritatively (.GENESIS principle 1).
/// </summary>
public sealed class DashedDirectiveResolutionTests : VaultTestBase
{
	private Task<StellarDirective> CreateDirectiveAsync(string title)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync(title, cancellationToken: TestContext.Current.CancellationToken));

	private Task<Objective> InitObjectiveAsync(string vaultRelativePath)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.InitializeFromPathAsync(vaultRelativePath, TestContext.Current.CancellationToken));

	[Fact]
	public async Task A_dashed_directive_folder_resolves_to_its_real_identity_not_the_title_prefix()
	{
		var directive = await CreateDirectiveAsync("2024 - Roadmap");

		var resolved = MarkdownFileLocator.TryResolveDirectivePuckFromDirectory(
			Vault.AbsolutePath("Directives/2024 - Roadmap"));

		Assert.Equal(directive.Id, resolved);
	}

	[Fact]
	public async Task An_objective_in_a_dashed_directives_partition_is_owned_by_that_directive()
	{
		var directive = await CreateDirectiveAsync("2024 - Roadmap");
		Vault.WriteVaultFile(
			"Directives/2024 - Roadmap/Objectives/Task.md",
			"# Task" + Environment.NewLine + "Body." + Environment.NewLine);

		var objective = await InitObjectiveAsync("Directives/2024 - Roadmap/Objectives/Task.md");

		Assert.Equal(directive.Id, objective.DirectiveId);
	}

	[Fact]
	public async Task An_objective_directly_in_a_dashed_directive_folder_is_owned_by_that_directive()
	{
		var directive = await CreateDirectiveAsync("2024 - Roadmap");
		Vault.WriteVaultFile(
			"Directives/2024 - Roadmap/Quick Note.md",
			"# Quick Note" + Environment.NewLine + "Body." + Environment.NewLine);

		var objective = await InitObjectiveAsync("Directives/2024 - Roadmap/Quick Note.md");

		Assert.Equal(directive.Id, objective.DirectiveId);
	}

	[Fact]
	public async Task A_directive_nested_under_a_dashed_parent_directive_resolves_its_parent()
	{
		var parent = await CreateDirectiveAsync("2024 - Roadmap");
		Vault.WriteVaultFile(
			"Directives/2024 - Roadmap/Strike/Strike.md",
			"# Strike" + Environment.NewLine + "Freeform child." + Environment.NewLine);

		var child = await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.InitializeFromPathAsync("Directives/2024 - Roadmap/Strike/Strike.md", TestContext.Current.CancellationToken));

		Assert.Equal(parent.Id, child.ParentDirectiveId);
	}
}
