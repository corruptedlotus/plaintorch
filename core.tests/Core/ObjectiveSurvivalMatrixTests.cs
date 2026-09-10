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
/// An objective whose note is already in place — standalone, directly in a directive, deep in a directive, in a
/// partition, under a nested directive, or under a freeform directive outside the entity root — must survive both a
/// startup sweep and a live reconcile, edited or not: neither the entity nor its note may be lost. This pins the
/// report of objectives vanishing from the database on startup while their files remained intact (an identity-resolution
/// hazard), across the full placement matrix.
/// </summary>
public sealed class ObjectiveSurvivalMatrixTests : VaultTestBase
{
	private const string EditMarker = "Edited while offline.";

	private Task<StellarDirective> CreateDirectiveAsync(string title)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync(title, cancellationToken: TestContext.Current.CancellationToken));

	private async Task<string> InitObjectiveAtAsync(string noteRelativePath)
	{
		Vault.WriteVaultFile(noteRelativePath, "# Task" + Environment.NewLine + "Body." + Environment.NewLine);
		var objective = await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.InitializeFromPathAsync(noteRelativePath, TestContext.Current.CancellationToken));
		return objective.Id;
	}

	private async Task RunModeAsync(string mode, string noteRelativePath, bool edit)
	{
		if (edit)
		{
			await File.AppendAllTextAsync(Vault.AbsolutePath(noteRelativePath), EditMarker + Environment.NewLine, TestContext.Current.CancellationToken);
		}

		if (mode == "sweep")
		{
			await Vault.SweepAsync();
		}
		else
		{
			await Vault.ReconcileAsync(Vault.AbsolutePath(noteRelativePath));
		}
	}

	private async Task AssertAliveAsync(string objectiveId, string noteRelativePath)
	{
		var stored = await Vault.QueryAsync(context => context.Incentives.AsNoTracking()
			.OfType<Objective>().SingleOrDefaultAsync(item => item.Id == objectiveId, TestContext.Current.CancellationToken));
		Assert.True(stored is not null, $"objective '{objectiveId}' must survive at '{noteRelativePath}'");
		Assert.True(Vault.VaultFileExists(noteRelativePath), $"note '{noteRelativePath}' must remain on disk");
	}

	public static IEnumerable<object[]> TopLevelPlacements()
	{
		string[] placements =
		[
			"Objectives/Task.md",                          // standalone in the objectives root
			"Directives/Campaign/Objectives/Task.md",      // directive's objectives partition
			"Directives/Campaign/Task.md",                 // directly in the directive folder
			"Directives/Campaign/Stuff/Task.md",           // a plain subfolder deep in the directive
		];
		foreach (var placement in placements)
		{
			foreach (var mode in new[] { "sweep", "runtime" })
			{
				foreach (var edit in new[] { false, true })
				{
					yield return [placement, mode, edit];
				}
			}
		}
	}

	[Theory]
	[MemberData(nameof(TopLevelPlacements))]
	public async Task An_objective_survives_across_placement_mode_and_edit(string placement, string mode, bool edit)
	{
		await CreateDirectiveAsync("Campaign");
		var objectiveId = await InitObjectiveAtAsync(placement);

		await RunModeAsync(mode, placement, edit);

		await AssertAliveAsync(objectiveId, placement);
	}

	[Theory]
	[InlineData("sweep", false)]
	[InlineData("sweep", true)]
	[InlineData("runtime", false)]
	public async Task An_objective_under_a_nested_directive_partition_survives(string mode, bool edit)
	{
		await CreateDirectiveAsync("Campaign");
		var strike = await CreateDirectiveAsync("Strike");
		// Re-parent Strike under Campaign by materializing it inside Campaign's folder.
		Vault.WriteVaultFile("Directives/Campaign/Strike/Strike.md", Vault.ReadVaultFile("Directives/Strike/Strike.md"));
		Directory.Delete(Vault.AbsolutePath("Directives/Strike"), recursive: true);
		_ = strike;

		var placement = "Directives/Campaign/Strike/Objectives/Task.md";
		var objectiveId = await InitObjectiveAtAsync(placement);

		await RunModeAsync(mode, placement, edit);

		await AssertAliveAsync(objectiveId, placement);
	}

	[Theory]
	[InlineData("sweep", false)]
	[InlineData("runtime", false)]
	public async Task An_objective_directly_in_a_nested_directive_survives(string mode, bool edit)
	{
		await CreateDirectiveAsync("Campaign");
		var strike = await CreateDirectiveAsync("Strike");
		Vault.WriteVaultFile("Directives/Campaign/Strike/Strike.md", Vault.ReadVaultFile("Directives/Strike/Strike.md"));
		Directory.Delete(Vault.AbsolutePath("Directives/Strike"), recursive: true);
		_ = strike;

		var placement = "Directives/Campaign/Strike/Task.md";
		var objectiveId = await InitObjectiveAtAsync(placement);

		await RunModeAsync(mode, placement, edit);

		await AssertAliveAsync(objectiveId, placement);
	}

	[Theory]
	[InlineData("sweep", false)]
	[InlineData("sweep", true)]
	[InlineData("runtime", false)]
	public async Task An_objective_under_a_freeform_directive_outside_the_entity_root_survives(string mode, bool edit)
	{
		// A freeform directive materialized at the vault root (outside Directives/).
		Vault.WriteVaultFile("Alpha/Alpha.md", "# Alpha" + Environment.NewLine);
		await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.InitializeFromPathAsync("Alpha/Alpha.md", TestContext.Current.CancellationToken));

		var placement = "Alpha/Objectives/Task.md";
		var objectiveId = await InitObjectiveAtAsync(placement);

		await RunModeAsync(mode, placement, edit);

		await AssertAliveAsync(objectiveId, placement);
	}

	[Theory]
	[InlineData("sweep", false)]
	[InlineData("sweep", true)]
	[InlineData("runtime", false)]
	public async Task A_dashed_title_objective_in_a_directive_survives(string mode, bool edit)
	{
		// The historical vanishing: before the Quiet-title fix, a dashed title had its "prefix - " purged on rewrite,
		// so the file was renamed out from under the recorded boundary and the orphan pass deleted the entity.
		await CreateDirectiveAsync("Campaign");
		var placement = "Directives/Campaign/Objectives/Q1 - Ship it.md";
		var objectiveId = await InitObjectiveAtAsync(placement);

		await RunModeAsync(mode, placement, edit);

		await AssertAliveAsync(objectiveId, placement);
	}

	[Theory]
	[InlineData("sweep")]
	[InlineData("runtime")]
	public async Task An_objective_in_a_dash_named_directive_survives(string mode)
	{
		await CreateDirectiveAsync("2024 - Roadmap");
		var placement = "Directives/2024 - Roadmap/Objectives/Task.md";
		var objectiveId = await InitObjectiveAtAsync(placement);

		await RunModeAsync(mode, placement, edit: false);

		await AssertAliveAsync(objectiveId, placement);
	}

	[Fact]
	public async Task A_dashed_title_materialized_objective_survives_the_startup_sweep()
	{
		// The exact reported shape: an objective materialized (boundary begun) into its partition, note in place, then a
		// startup sweep. Its dashed title must not be purged, or the boundary path would diverge and the entity vanish.
		var directive = await CreateDirectiveAsync("Campaign");
		var objective = await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.CreateStandaloneAsync("Q1 - Ship it", cancellationToken: TestContext.Current.CancellationToken));
		await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.UpdateAsync(objective.Id, new ObjectiveUpdate(DirectiveId: directive.Id), TestContext.Current.CancellationToken));
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);

		var placement = "Directives/Campaign/Objectives/Q1 - Ship it.md";
		Assert.True(Vault.VaultFileExists(placement), "precondition: materialized under its full dashed title");

		await Vault.SweepAsync();

		await AssertAliveAsync(objective.Id, placement);
	}
}
