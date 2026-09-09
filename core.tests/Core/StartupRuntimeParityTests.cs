using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Startup / wakeup / runtime parity: a whole-vault sweep must reach the same final database and vault state as the
/// equivalent changes reconciled incrementally as live-watcher events — and it must do so whether the sweep runs on a
/// cold start or when the watcher wakes from a structural-access sleep. The wakeup sweep is not a distinct operation:
/// the service re-enters the very same startup scan (<c>RunStartupScanAsync</c>) on waking that it runs on boot, which
/// the harness models with <see cref="TestVault.SweepAsync"/>. So a "wakeup" is a first sweep, then offline drift while
/// asleep, then a second sweep — and it must land exactly where a cold startup seeing that drift lands, and where
/// incremental runtime events land. A divergence would mean the watcher reconciles a while-asleep change differently
/// after waking than it would at boot or in real time — a data-integrity hazard.
/// </summary>
public sealed class StartupRuntimeParityTests
{
	private const string ObjectiveId = "j00000001";
	private const string ObjectiveTitle = "Ship it";
	private const string ObjectiveRelativePath = "Objectives/Ship it.md";

	/// <summary>
	/// Runs one scenario — a pre-drift <paramref name="seed"/> (authored through the API) and a <paramref name="drift"/>
	/// applied as raw while-offline file changes (returning the affected paths in event order) — three ways in fresh,
	/// isolated vaults, and returns the three final snapshots:
	/// <list type="bullet">
	/// <item><description><c>Startup</c>: seed, drift, then one cold sweep that sees everything at once.</description></item>
	/// <item><description><c>Wakeup</c>: seed, an initial sweep (the pre-sleep steady state), then drift, then a second
	/// sweep — the watcher waking to reconcile changes made while it slept.</description></item>
	/// <item><description><c>Runtime</c>: seed, drift, then the drift reconciled as incremental live events.</description></item>
	/// </list>
	/// </summary>
	private static async Task<(string Startup, string Wakeup, string Runtime)> RunAllWaysAsync(
		Func<TestVault, Task> seed,
		Func<TestVault, Task<IReadOnlyList<string>>> drift)
	{
		var startupVault = new TestVault();
		var wakeupVault = new TestVault();
		var runtimeVault = new TestVault();
		await startupVault.InitializeAsync();
		await wakeupVault.InitializeAsync();
		await runtimeVault.InitializeAsync();
		try
		{
			// Cold startup: everything is already on disk when the one and only sweep runs.
			await seed(startupVault);
			await drift(startupVault);
			await startupVault.SweepAsync();

			// Wakeup: sweep once over the clean pre-sleep state, then apply the while-asleep drift, then sweep again.
			// The first sweep is what makes this a wakeup rather than a boot — any state it leaves behind (begun
			// boundaries, write-barrier entries, raised statuses) must not change how the second sweep reconciles.
			await seed(wakeupVault);
			await wakeupVault.SweepAsync();
			await drift(wakeupVault);
			await wakeupVault.SweepAsync();

			// Runtime: the drift arrives as incremental events.
			await seed(runtimeVault);
			foreach (var path in await drift(runtimeVault))
			{
				await runtimeVault.ReconcileAsync(path);
			}

			return (
				await SnapshotAsync(startupVault),
				await SnapshotAsync(wakeupVault),
				await SnapshotAsync(runtimeVault));
		}
		finally
		{
			await runtimeVault.DisposeAsync();
			await wakeupVault.DisposeAsync();
			await startupVault.DisposeAsync();
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

		// Auto-generated PUCK ids are random per vault, so they cannot be compared across the runs. Map each id to a
		// placeholder keyed by its stable business identity (type + title), then normalize every occurrence — including
		// the id embedded in file frontmatter — so parity is asserted on logical state, not on the ids.
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

	private static async Task SeedBegunObjectiveAsync(TestVault vault)
	{
		var objective = await vault.SeedStandaloneObjectiveAsync(ObjectiveTitle, ObjectiveId);
		await vault.BeginObjectiveBoundaryAsync(objective.Id); // materializes the quiet file on disk
	}

	private static IReadOnlyList<string> ObjectivePaths(TestVault vault) => [vault.AbsolutePath(ObjectiveRelativePath)];

	[Fact]
	public async Task An_unchanged_begun_objective_is_stable_across_startup_wakeup_and_runtime()
	{
		// Idempotence: with no file change, none of a cold sweep, a wakeup's second sweep, or an incremental reconcile
		// alters the state, and all three leave it identical. A watcher that "reflects" spuriously on an untouched file
		// — or that behaves differently on its second sweep — would diverge here.
		var (startup, wakeup, runtime) = await RunAllWaysAsync(
			SeedBegunObjectiveAsync,
			vault => Task.FromResult(ObjectivePaths(vault)));

		Assert.Equal(startup, wakeup);
		Assert.Equal(startup, runtime);
	}

	[Fact]
	public async Task Editing_a_begun_objective_file_reconciles_identically_at_startup_wakeup_and_runtime()
	{
		var (startup, wakeup, runtime) = await RunAllWaysAsync(
			SeedBegunObjectiveAsync,
			async vault =>
			{
				// A while-offline edit to the managed file's body.
				var absolute = vault.AbsolutePath(ObjectiveRelativePath);
				await File.AppendAllTextAsync(absolute, "Extra body line added while offline." + Environment.NewLine, TestContext.Current.CancellationToken);
				return ObjectivePaths(vault);
			});

		Assert.Equal(startup, wakeup);
		Assert.Equal(startup, runtime);
	}

	[Fact]
	public async Task Deleting_a_begun_objective_file_reconciles_identically_at_startup_wakeup_and_runtime()
	{
		// The trickiest wakeup case: the initial sweep confirms the begun boundary, then the file is deleted while the
		// watcher sleeps. The wakeup sweep's orphan pass must still recover the identity and delete the entity — exactly
		// as a cold startup or a live delete event would — rather than being thrown off by the earlier sweep's state.
		var (startup, wakeup, runtime) = await RunAllWaysAsync(
			SeedBegunObjectiveAsync,
			vault =>
			{
				File.Delete(vault.AbsolutePath(ObjectiveRelativePath));
				return Task.FromResult(ObjectivePaths(vault));
			});

		Assert.Equal(startup, wakeup);
		Assert.Equal(startup, runtime);
	}
}
