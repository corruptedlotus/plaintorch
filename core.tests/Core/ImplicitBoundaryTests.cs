using Microsoft.EntityFrameworkCore;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Watcher;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// PEP091 implicit boundary: no file on create, materialize on begin, boundary-authoritative deletion.
/// </summary>
public sealed class ImplicitBoundaryTests : VaultTestBase
{
	[Fact]
	public async Task Create_does_not_materialize_a_file_but_persists_the_entity()
	{
		var objective = await Vault.SeedStandaloneObjectiveAsync("Ship it");

		Assert.Empty(Vault.MarkdownFilesUnder(Vault.Layout.ObjectivesRoot));
		Assert.True(await Vault.QueryAsync(context => context.Objectives.AnyAsync(item => item.Id == objective.Id)));
	}

	[Fact]
	public async Task Begin_materializes_a_quiet_file_with_puck_frontmatter()
	{
		var objective = await Vault.SeedStandaloneObjectiveAsync("Ship it");

		await Vault.BeginObjectiveBoundaryAsync(objective.Id);

		Assert.True(Vault.VaultFileExists("Objectives/Ship it.md"));
		Assert.Contains($"puck: {objective.Id}", Vault.ReadVaultFile("Objectives/Ship it.md"));
	}

	[Fact]
	public async Task Deleting_a_boundary_begun_file_is_authoritative()
	{
		var objective = await Vault.SeedStandaloneObjectiveAsync("Ship it");
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);
		var path = Vault.AbsolutePath("Objectives/Ship it.md");
		File.Delete(path);

		var candidate = await Vault.ReconcileAsync(path);

		Assert.NotNull(candidate);
		Assert.Equal(VaultSyncAction.DeleteFromDatabase, candidate!.SuggestedAction);
		Assert.False(await Vault.QueryAsync(context => context.Objectives.AnyAsync(item => item.Id == objective.Id)));
	}

	[Fact]
	public async Task Objective_without_a_begun_boundary_survives_a_scan()
	{
		var objective = await Vault.SeedStandaloneObjectiveAsync("Ship it");

		await Vault.ScanAsync();

		Assert.True(await Vault.QueryAsync(context => context.Objectives.AnyAsync(item => item.Id == objective.Id)));
	}
}
