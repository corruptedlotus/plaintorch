using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Startup/runtime parity: a startup sweep (one-shot discover-and-reconcile of the whole vault) must reach the same
/// final database and vault state that the equivalent changes would produce as incremental live-watcher events. Any
/// divergence means a change made while the daemon was offline is reconciled differently at next startup than it
/// would have been in real time — a data-integrity hazard. Each scenario runs in two fresh, isolated vaults: one
/// reconciled by <see cref="TestVault.SweepAsync"/>, one by a sequence of <see cref="TestVault.ReconcileAsync"/>
/// events, and the two final snapshots are compared.
/// </summary>
public sealed class StartupRuntimeParityTests
{
	private const string ObjectiveId = "j00000001";
	private const string ObjectiveTitle = "Ship it";
	private const string ObjectiveRelativePath = "Objectives/Ship it.md";

	/// <summary>
	/// Runs an arrange step (seed deterministic entities via the API, then author raw file changes needing
	/// reconciliation, returning the affected absolute paths in event order) in two fresh vaults: the first is
	/// reconciled by a startup sweep, the second by incremental runtime events. Returns both final snapshots.
	/// </summary>
	private static async Task<(string Sweep, string Runtime)> RunBothWaysAsync(
		Func<TestVault, Task<IReadOnlyList<string>>> arrange)
	{
		var sweepVault = new TestVault();
		var runtimeVault = new TestVault();
		await sweepVault.InitializeAsync();
		await runtimeVault.InitializeAsync();
		try
		{
			await arrange(sweepVault);
			await sweepVault.SweepAsync();

			var runtimeEvents = await arrange(runtimeVault);
			foreach (var path in runtimeEvents)
			{
				await runtimeVault.ReconcileAsync(path);
			}

			return (await SnapshotAsync(sweepVault), await SnapshotAsync(runtimeVault));
		}
		finally
		{
			await runtimeVault.DisposeAsync();
			await sweepVault.DisposeAsync();
		}
	}

	private static async Task<string> SnapshotAsync(TestVault vault)
	{
		var entities = await vault.QueryAsync(async context =>
		{
			var rows = new List<(string Type, string Id, string Title)>();
			rows.AddRange((await context.Incentives.AsNoTracking()
					.Select(item => new { item.Id, item.Title })
					.ToListAsync(TestContext.Current.CancellationToken))
				.Select(item => ("Incentive", item.Id, item.Title)));
			rows.AddRange((await context.Directives.AsNoTracking()
					.Select(item => new { item.Id, item.Title })
					.ToListAsync(TestContext.Current.CancellationToken))
				.Select(item => ("Directive", item.Id, item.Title)));
			return rows;
		});

		// Auto-generated PUCK ids are random per vault, so they cannot be compared across the two runs. Map each id
		// to a placeholder keyed by its stable business identity (type + title), then normalize every occurrence —
		// including the id embedded in file frontmatter — so parity is asserted on logical state, not on the ids.
		var ordered = entities
			.OrderBy(entity => entity.Type, StringComparer.Ordinal)
			.ThenBy(entity => entity.Title, StringComparer.Ordinal)
			.ToList();
		var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
		for (var index = 0; index < ordered.Count; index++)
		{
			idMap[ordered[index].Id] = $"<ID{index}>";
		}

		string Normalize(string value)
		{
			foreach (var (id, placeholder) in idMap)
			{
				value = value.Replace(id, placeholder, StringComparison.Ordinal);
			}

			return value;
		}

		var entityRows = ordered.Select(entity => Normalize($"{entity.Type}|{entity.Id}|{entity.Title}"));

		var fileRows = Directory.EnumerateFiles(vault.VaultRoot, "*.md", SearchOption.AllDirectories)
			.Where(path => !path.Contains(".plaintorch", StringComparison.OrdinalIgnoreCase))
			.Select(path => Normalize($"FILE|{Path.GetRelativePath(vault.VaultRoot, path).Replace('\\', '/')}::{File.ReadAllText(path)}"));

		return string.Join("\n", entityRows.Concat(fileRows).OrderBy(row => row, StringComparer.Ordinal));
	}

	private static async Task<Objective> SeedBegunObjectiveAsync(TestVault vault)
	{
		var objective = await vault.SeedStandaloneObjectiveAsync(ObjectiveTitle, ObjectiveId);
		await vault.BeginObjectiveBoundaryAsync(objective.Id); // materializes the quiet file on disk
		return objective;
	}

	[Fact]
	public async Task An_unchanged_begun_objective_is_stable_across_a_sweep_and_runtime()
	{
		// Idempotence: with no file change, neither a startup sweep nor an incremental reconcile alters the state,
		// and both leave it identical. A watcher that "reflects" spuriously on an untouched file would diverge here.
		var (sweep, runtime) = await RunBothWaysAsync(async vault =>
		{
			await SeedBegunObjectiveAsync(vault);
			return new[] { vault.AbsolutePath(ObjectiveRelativePath) };
		});

		Assert.Equal(sweep, runtime);
	}

	[Fact]
	public async Task Editing_a_begun_objective_file_reconciles_identically_at_startup_and_at_runtime()
	{
		var (sweep, runtime) = await RunBothWaysAsync(async vault =>
		{
			await SeedBegunObjectiveAsync(vault);

			// A while-offline edit to the managed file's body.
			var absolute = vault.AbsolutePath(ObjectiveRelativePath);
			await File.AppendAllTextAsync(absolute, "Extra body line added while offline." + Environment.NewLine, TestContext.Current.CancellationToken);
			return new[] { absolute };
		});

		Assert.Equal(sweep, runtime);
	}

	[Fact] // Phase 4: fixed — ScanAsync's orphan pass reconciles boundary-begun files that vanished while offline.
	public async Task Deleting_a_begun_objective_file_reconciles_identically_at_startup_and_at_runtime()
	{
		var (sweep, runtime) = await RunBothWaysAsync(async vault =>
		{
			await SeedBegunObjectiveAsync(vault);

			// A while-offline deletion of a boundary-begun file, which is authoritative for implicit entities.
			var absolute = vault.AbsolutePath(ObjectiveRelativePath);
			File.Delete(absolute);
			return new[] { absolute };
		});

		Assert.Equal(sweep, runtime);
	}
}
