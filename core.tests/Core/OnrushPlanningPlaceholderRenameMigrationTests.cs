using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Puck;
using Pleiades.Tests.Harness;
using Pleiades.Vault;
using Pleiades.Vault.Database;
using Pleiades.Vault.Migration;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// v2→v3 vault migration: an old-style <c>0</c> in-planning onrush placeholder is carried across to the gate-passing
/// PUCK <c>x0000</c> — its row identity, the foreign keys of the objective and checkpoint it tracks, its PUCK registry
/// entry, and its self-named directory on disk.
/// </summary>
public sealed class OnrushPlanningPlaceholderRenameMigrationTests : VaultTestBase
{
	[Fact]
	public async Task Migrates_legacy_planning_placeholder_to_gate_passing_identity()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		// Arrange: an old-style '0' planning placeholder exactly as a pre-v3 vault held it — the sprint, its milestone
		// checkpoint (mutual nullable FK, as PlanAsync creates it), a tracked objective, a PUCK registry row (Index
		// storage resolved the non-tokenizable id through it), and a materialized self-named directory on disk.
		var objective = await Vault.SeedStandaloneObjectiveAsync("Tracked objective");
		await Vault.WithScopeAsync(async services =>
		{
			var context = services.GetRequiredService<PlainfraContext>();
			var storage = services.GetRequiredService<PlaintorchMarkdownStorageService>();

			// Insert the sprint and its milestone first (only the checkpoint→sprint foreign key), then wire the
			// reciprocal milestone key once both rows exist — the placeholder's mutual reference cannot be inserted
			// in a single command batch.
			var sprint = new OnrushSprint { Id = "0", Title = "Legacy Plan" };
			var milestone = new Checkpoint { Id = "c0001", Title = "Legacy Plan milestone", OnrushSprintId = "0" };
			context.OnrushSprints.Add(sprint);
			context.Checkpoints.Add(milestone);
			await context.SaveChangesAsync(cancellationToken);

			sprint.MilestoneCheckpointId = milestone.Id;
			var tracked = await context.Objectives.FirstAsync(item => item.Id == objective.Id, cancellationToken);
			tracked.OnrushSprintId = "0";
			context.PuckRegistryEntries.Add(new PuckRegistryEntry
			{
				Id = "0",
				Declaration = "onrush-sprint",
				IssuedUtc = DateTimeOffset.UtcNow,
			});
			await context.SaveChangesAsync(cancellationToken);

			await storage.SaveOnrushSprintAsync(sprint, cancellationToken: cancellationToken);
		});

		Assert.Contains(
			Directory.GetDirectories(Vault.Layout.OnrushRoot).Select(Path.GetFileName),
			name => name == "0 - Legacy Plan");

		// Downgrade the stored version so the runner applies the v2→v3 migration (and only that one).
		var settings = VaultSettings.Load(Vault.Layout.SettingsPath);
		settings.SchemaVersion = 2;
		settings.Save(Vault.Layout.SettingsPath);

		// Act
		await Vault.WithScopeAsync(services => services.GetRequiredService<VaultMigrationRunner>().RunAsync());

		// Assert: the bare '0' identity is gone everywhere; 'x0000' carries the placeholder, its milestone, its tracked
		// objective, and its registry entry.
		var state = await Vault.QueryAsync(async context => new
		{
			OldRow = await context.OnrushSprints.AsNoTracking().AnyAsync(sprint => sprint.Id == "0", cancellationToken),
			Renamed = await context.OnrushSprints.AsNoTracking()
				.FirstOrDefaultAsync(sprint => sprint.Id == OnrushSprint.PlanningPlaceholderId, cancellationToken),
			MilestoneOwner = (await context.Checkpoints.AsNoTracking().FirstAsync(item => item.Id == "c0001", cancellationToken)).OnrushSprintId,
			ObjectiveOwner = (await context.Objectives.AsNoTracking().FirstAsync(item => item.Id == objective.Id, cancellationToken)).OnrushSprintId,
			OldRegistry = await context.PuckRegistryEntries.AsNoTracking().AnyAsync(entry => entry.Id == "0", cancellationToken),
			NewRegistry = await context.PuckRegistryEntries.AsNoTracking()
				.AnyAsync(entry => entry.Id == OnrushSprint.PlanningPlaceholderId, cancellationToken),
		});

		Assert.False(state.OldRow);
		Assert.NotNull(state.Renamed);
		Assert.Equal("Legacy Plan", state.Renamed!.Title);
		Assert.Equal("c0001", state.Renamed.MilestoneCheckpointId);
		Assert.Equal(OnrushSprint.PlanningPlaceholderId, state.MilestoneOwner);
		Assert.Equal(OnrushSprint.PlanningPlaceholderId, state.ObjectiveOwner);
		Assert.False(state.OldRegistry);
		Assert.True(state.NewRegistry);

		// And the self-named directory was renamed on disk, note and all.
		var directories = Directory.GetDirectories(Vault.Layout.OnrushRoot).Select(Path.GetFileName).ToList();
		Assert.DoesNotContain("0 - Legacy Plan", directories);
		Assert.Contains("x0000 - Legacy Plan", directories);
		Assert.True(File.Exists(Path.Combine(Vault.Layout.OnrushRoot, "x0000 - Legacy Plan", "x0000 - Legacy Plan.md")));

		Assert.Equal(VaultSchema.CurrentVersion, VaultSettings.Load(Vault.Layout.SettingsPath).SchemaVersion);
	}
}
