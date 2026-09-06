using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Watcher;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Incentive notes hosted by a folder-shaped directive. Detection is policy-driven: an identity-driven child
/// (implicit/freeform) is detected anywhere inside its parent — deep in the partition, or directly in the directive
/// folder — and initialises owned by that directive. Detection no longer relocates it: the partition is only the
/// default location for a fresh materialisation, not an authoritative one a note is moved into.
/// </summary>
public sealed class IncentivePlacementTests : VaultTestBase
{
	private Task<StellarDirective> CreateDirectiveAsync(string title)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync(title, cancellationToken: TestContext.Current.CancellationToken));

	private Task<Objective> InitObjectiveAsync(string vaultRelativePath)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.InitializeFromPathAsync(vaultRelativePath, TestContext.Current.CancellationToken));

	[Fact]
	public async Task Deeply_nested_note_in_the_partition_initialises_owned_by_the_directive_and_stays()
	{
		var directive = await CreateDirectiveAsync("Campaign");
		Vault.WriteVaultFile(
			"Directives/Campaign/Objectives/Backlog/Deep Task.md",
			"# Deep Task" + Environment.NewLine + "Authored deep in the partition." + Environment.NewLine);

		var objective = await InitObjectiveAsync("Directives/Campaign/Objectives/Backlog/Deep Task.md");

		// Deep-hierarchy detection: the note is owned by its hosting directive even nested below the partition root.
		Assert.Equal(directive.Id, objective.DirectiveId);
		Assert.Equal("Deep Task", objective.Title);
		// And it is kept where it was authored, not relocated to the partition root.
		Assert.True(Vault.VaultFileExists("Directives/Campaign/Objectives/Backlog/Deep Task.md"));
		Assert.False(Vault.VaultFileExists("Directives/Campaign/Objectives/Deep Task.md"));
	}

	[Fact]
	public async Task Note_directly_in_the_directive_folder_initialises_owned_by_the_directive_without_moving()
	{
		var directive = await CreateDirectiveAsync("Campaign");
		Vault.WriteVaultFile(
			"Directives/Campaign/Quick Note.md",
			"# Quick Note" + Environment.NewLine + "Right in the directive folder." + Environment.NewLine);

		var objective = await InitObjectiveAsync("Directives/Campaign/Quick Note.md");

		Assert.Equal(directive.Id, objective.DirectiveId);
		// The auto-move is called off: the note stays in the directive folder rather than being pulled into Objectives/.
		Assert.True(Vault.VaultFileExists("Directives/Campaign/Quick Note.md"));
		Assert.False(Vault.VaultFileExists("Directives/Campaign/Objectives/Quick Note.md"));
	}

	[Fact]
	public async Task A_kept_note_rewrites_in_place_on_update_rather_than_orphaning_a_partition_copy()
	{
		var directive = await CreateDirectiveAsync("Campaign");
		Vault.WriteVaultFile(
			"Directives/Campaign/Quick Note.md",
			"# Quick Note" + Environment.NewLine + "body" + Environment.NewLine);
		var objective = await InitObjectiveAsync("Directives/Campaign/Quick Note.md");

		var renamed = await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.UpdateAsync(objective.Id, new ObjectiveUpdate(Title: "Renamed Note"), TestContext.Current.CancellationToken));
		Assert.Equal("Renamed Note", renamed.Title);

		// A later edit rewrites the real file beside the original (new title-derived name, same directory): the old file
		// is gone and no stray copy is left at the canonical partition path.
		Assert.True(Vault.VaultFileExists("Directives/Campaign/Renamed Note.md"));
		Assert.False(Vault.VaultFileExists("Directives/Campaign/Quick Note.md"));
		Assert.False(Vault.VaultFileExists("Directives/Campaign/Objectives/Renamed Note.md"));
	}

	[Fact]
	public async Task A_new_directive_hosted_objective_materialises_in_the_partition_by_default()
	{
		var directive = await CreateDirectiveAsync("Campaign");
		var objective = await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.CreateStandaloneAsync("Fresh Task", cancellationToken: TestContext.Current.CancellationToken));
		await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.UpdateAsync(objective.Id, new ObjectiveUpdate(DirectiveId: directive.Id), TestContext.Current.CancellationToken));

		// A brand-new incentive with no authored file defaults to the partition when its boundary materialises it.
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);

		Assert.True(Vault.VaultFileExists("Directives/Campaign/Objectives/Fresh Task.md"));
	}

	[Fact]
	public async Task A_note_under_a_sibling_partition_classifies_as_that_sibling_kind_not_this_one()
	{
		await CreateDirectiveAsync("Campaign");
		Vault.WriteVaultFile("Directives/Campaign/Fates/Some Fate.md", "# Some Fate" + Environment.NewLine);
		var fatePath = Vault.AbsolutePath("Directives/Campaign/Fates/Some Fate.md");

		var entityType = await Vault.WithScopeAsync(services =>
		{
			var catalog = services.GetRequiredService<VaultPathSyncModelCatalog>();
			return Task.FromResult(catalog.TryResolve(fatePath, out var model) ? model!.EntityType : null);
		});

		// The enclosing Fates partition disambiguates the note to the fate kind; the objective model excludes it.
		Assert.Equal(typeof(Fate), entityType);
	}
}
