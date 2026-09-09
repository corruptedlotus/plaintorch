using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Quiet storage keeps the PUCK in frontmatter and the whole filename is the title, so a title that legitimately
/// contains the canonical " - " separator (e.g. "Q1 - Ship it") must be read back intact. The reader is now symmetric
/// with the writer (<c>VaultStoragePathComposer.GetBaseName</c> writes Quiet names title-only), so the watcher no longer
/// mis-splits the title into a phantom "{token} - {title}" identity and purges the "prefix - " on the next rewrite —
/// the watcher must not exert authority over a name it merely misunderstood (.GENESIS principle 1).
/// </summary>
public sealed class QuietDashedTitleTests : VaultTestBase
{
	private Task<Objective> StoredAsync(string id)
		=> Vault.QueryAsync(context => context.Incentives.AsNoTracking()
			.OfType<Objective>().SingleAsync(item => item.Id == id, TestContext.Current.CancellationToken));

	[Fact]
	public async Task A_dashed_quiet_title_survives_reconcile_without_purging_its_prefix()
	{
		var objective = await Vault.SeedStandaloneObjectiveAsync("Q1 - Ship it", "j00000abc");
		await Vault.BeginObjectiveBoundaryAsync(objective.Id); // materialize the quiet (title-only) file

		Assert.True(Vault.VaultFileExists("Objectives/Q1 - Ship it.md"), "precondition: materializes under the full dashed title");

		// The watcher observes its own materialized file.
		await Vault.ReconcileAsync(Vault.AbsolutePath("Objectives/Q1 - Ship it.md"));

		Assert.Equal("Q1 - Ship it", (await StoredAsync(objective.Id)).Title);
		Assert.True(Vault.VaultFileExists("Objectives/Q1 - Ship it.md"), "the dashed file must not be renamed");
		Assert.False(Vault.VaultFileExists("Objectives/Ship it.md"), "the prefix must not be purged into a title-only file");
	}

	[Fact]
	public async Task Renaming_a_quiet_entity_to_a_dashed_title_is_reconciled_without_purging_the_prefix()
	{
		// The reported flow: an existing entity is renamed to a "prefix - stuff" shape, which renames its file.
		var objective = await Vault.SeedStandaloneObjectiveAsync("Ship it", "j00000abc");
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);

		await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.UpdateAsync(objective.Id, new ObjectiveUpdate(Title: "Q1 - Ship it"), TestContext.Current.CancellationToken));

		Assert.True(Vault.VaultFileExists("Objectives/Q1 - Ship it.md"), "the rename lands the file under the new dashed title");
		Assert.False(Vault.VaultFileExists("Objectives/Ship it.md"), "the old file is gone after the rename");

		// The watcher then observes the renamed file; it must not undo the rename by purging the prefix.
		await Vault.ReconcileAsync(Vault.AbsolutePath("Objectives/Q1 - Ship it.md"));

		Assert.Equal("Q1 - Ship it", (await StoredAsync(objective.Id)).Title);
		Assert.True(Vault.VaultFileExists("Objectives/Q1 - Ship it.md"));
		Assert.False(Vault.VaultFileExists("Objectives/Ship it.md"));
	}

	[Fact]
	public async Task A_quiet_title_with_multiple_separators_is_preserved_whole()
	{
		var objective = await Vault.SeedStandaloneObjectiveAsync("Q1 - Ship - the thing", "j00000abc");
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);

		await Vault.ReconcileAsync(Vault.AbsolutePath("Objectives/Q1 - Ship - the thing.md"));

		Assert.Equal("Q1 - Ship - the thing", (await StoredAsync(objective.Id)).Title);
		Assert.True(Vault.VaultFileExists("Objectives/Q1 - Ship - the thing.md"));
	}
}
