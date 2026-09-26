using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Startup / wakeup / runtime parity: a whole-vault sweep must reach the same final database and vault state as the
/// equivalent changes reconciled incrementally as live-watcher events — and it must do so whether the sweep runs on a
/// cold start or when the watcher wakes from a structural-access sleep. The wakeup sweep is not a distinct operation:
/// the service re-enters the very same startup scan (<c>RunStartupScanAsync</c>) on waking that it runs on boot. So a
/// "wakeup" is a first sweep, then offline drift while asleep, then a second sweep — and it must land exactly where a
/// cold startup seeing that drift lands, and where incremental runtime events land. A divergence would mean the watcher
/// reconciles a while-asleep change differently after waking than it would at boot or in real time — a data-integrity
/// hazard. Every leg drives the production reconciler: the sweep through <see cref="TestVault.SweepWithIssuesAsync"/>,
/// the runtime events through <see cref="TestVault.ReconcileEventsWithIssuesAsync"/>, which mirrors the watcher's drain
/// (each changed path, then one vanished-note check) — so the runtime leg cannot drift from the watcher it models.
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
			await startupVault.SweepWithIssuesAsync();

			// Wakeup: sweep once over the clean pre-sleep state, then apply the while-asleep drift, then sweep again.
			// The first sweep is what makes this a wakeup rather than a boot — any state it leaves behind (begun
			// boundaries, write-barrier entries, raised statuses) must not change how the second sweep reconciles.
			await seed(wakeupVault);
			await wakeupVault.SweepWithIssuesAsync();
			await drift(wakeupVault);
			await wakeupVault.SweepWithIssuesAsync();

			// Runtime: the drift arrives as the events the watcher would drain, in one batch.
			await seed(runtimeVault);
			await runtimeVault.ReconcileEventsWithIssuesAsync(await drift(runtimeVault));

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

		// A note whose entity is gone (moved into an ignored folder, say) keeps asserting its random id, which no entity maps.
		string NormalizeFile(string value)
			=> System.Text.RegularExpressions.Regex.Replace(Normalize(value), @"puck: (?!<ID)\S+", "puck: <GONE>");

		var entityRows = ordered.Select(entity => Normalize($"{entity.Type}|{entity.Id}|{entity.Title}"));

		var fileRows = Directory.EnumerateFiles(vault.VaultRoot, "*.md", SearchOption.AllDirectories)
			.Where(path => !path.Contains(".plaintorch", StringComparison.OrdinalIgnoreCase))
			.Select(path => NormalizeFile($"FILE|{Path.GetRelativePath(vault.VaultRoot, path).Replace('\\', '/')}::{File.ReadAllText(path)}"));

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
	public async Task The_orphan_pass_leaves_a_boundary_whose_entity_is_already_gone()
	{
		// A begun boundary is an audit entry, so it outlives the delete it enabled. Once the entity and its file are both
		// gone there is nothing left to reconcile, and the sweep must not re-inspect the dead path on every startup.
		var vault = new TestVault();
		await vault.InitializeAsync();
		try
		{
			await SeedBegunObjectiveAsync(vault);
			File.Delete(vault.AbsolutePath(ObjectiveRelativePath));
			await vault.SweepAsync(); // the orphan pass deletes the entity
			Assert.Equal(0, await vault.QueryAsync(context => context.Incentives.CountAsync(TestContext.Current.CancellationToken)));

			var next = await vault.ScanAsync();
			Assert.DoesNotContain(next.Candidates, candidate => string.Equals(candidate.AbsolutePath, vault.AbsolutePath(ObjectiveRelativePath), StringComparison.OrdinalIgnoreCase));
		}
		finally
		{
			await vault.DisposeAsync();
		}
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

	// --- identity withdrawn from the vault (no path is recorded anywhere; a note is found gone by identity) ---

	private const string DirectiveTitle = "Realm";
	private const string DirectiveFolder = "Directives/Realm";
	private const string DirectiveRelativePath = "Directives/Realm/Realm.md";

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private static async Task SeedBegunObjectiveInFolderAsync(TestVault vault)
	{
		await SeedBegunObjectiveAsync(vault);
		Directory.CreateDirectory(vault.AbsolutePath("Objectives/Batch"));
		File.Move(vault.AbsolutePath(ObjectiveRelativePath), vault.AbsolutePath("Objectives/Batch/Ship it.md"));
	}

	private static Task<StellarDirective> SeedDirectiveAsync(TestVault vault)
		=> vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>().CreateStandaloneAsync(DirectiveTitle, cancellationToken: Ct));

	private static async Task SeedDirectiveWithObjectiveAsync(TestVault vault, bool begun)
	{
		var directive = await SeedDirectiveAsync(vault);
		var objective = await vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.CreateFromDirectiveAsync(directive.Id, "Quest", cancellationToken: Ct));
		if (begun)
		{
			await vault.BeginObjectiveBoundaryAsync(objective.Id);
		}
	}

	/// <summary>Rewrites a note without its <c>puck</c> line, leaving the rest as the user wrote it.</summary>
	private static IReadOnlyList<string> StripIdentity(TestVault vault, string relativePath, string? replacement = null)
	{
		var path = vault.AbsolutePath(relativePath);
		var lines = File.ReadAllLines(path)
			.Select(line => line.TrimStart().StartsWith("puck:", StringComparison.Ordinal) ? replacement : line)
			.Where(line => line is not null);
		File.WriteAllLines(path, lines!);
		return [path];
	}

	private static IReadOnlyList<string> Move(TestVault vault, string from, string to, bool directory = false)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(vault.AbsolutePath(to))!);
		if (directory)
		{
			Directory.Move(vault.AbsolutePath(from), vault.AbsolutePath(to));
		}
		else
		{
			File.Move(vault.AbsolutePath(from), vault.AbsolutePath(to));
		}

		return [vault.AbsolutePath(from), vault.AbsolutePath(to)];
	}

	private static async Task AssertParityAsync(Func<TestVault, Task> seed, Func<TestVault, IReadOnlyList<string>> drift, Action<string> expect)
	{
		var (startup, wakeup, runtime) = await RunAllWaysAsync(seed, vault => Task.FromResult(drift(vault)));
		expect(startup);
		Assert.Equal(startup, wakeup);
		Assert.Equal(startup, runtime);
	}

	[Fact]
	public async Task Removing_a_begun_notes_identity_deletes_the_objective_and_keeps_the_note_alike_at_startup_wakeup_and_runtime()
		=> await AssertParityAsync(
			SeedBegunObjectiveAsync,
			vault => StripIdentity(vault, ObjectiveRelativePath),
			snapshot =>
			{
				Assert.DoesNotContain("Incentive|", snapshot);
				Assert.Contains("FILE|Objectives/Ship it.md::", snapshot);
			});

	[Fact]
	public async Task Changing_a_begun_notes_identity_deletes_the_objective_alike_at_startup_wakeup_and_runtime()
		=> await AssertParityAsync(
			SeedBegunObjectiveAsync,
			vault => StripIdentity(vault, ObjectiveRelativePath, "puck: j99999999"),
			snapshot =>
			{
				Assert.DoesNotContain("Incentive|", snapshot);
				Assert.Contains("FILE|Objectives/Ship it.md::", snapshot);
			});

	[Fact]
	public async Task Moving_a_begun_note_into_an_ignored_folder_deletes_the_objective_alike_at_startup_wakeup_and_runtime()
		=> await AssertParityAsync(
			SeedBegunObjectiveAsync,
			vault => Move(vault, ObjectiveRelativePath, "_archive/Ship it.md"),
			snapshot => Assert.DoesNotContain("Incentive|", snapshot));

	[Fact]
	public async Task Moving_a_begun_note_elsewhere_keeps_the_objective_alike_at_startup_wakeup_and_runtime()
		=> await AssertParityAsync(
			SeedBegunObjectiveAsync,
			vault => Move(vault, ObjectiveRelativePath, "Objectives/Moved/Ship it.md"),
			snapshot => Assert.Contains("Incentive|<ID0>|Ship it", snapshot));

	[Fact]
	public async Task Deleting_a_folder_of_begun_notes_as_one_event_deletes_them_alike_at_startup_wakeup_and_runtime()
		=> await AssertParityAsync(
			SeedBegunObjectiveInFolderAsync,
			vault =>
			{
				// A folder deleted through the system trash raises one event, for the folder alone.
				Directory.Delete(vault.AbsolutePath("Objectives/Batch"), recursive: true);
				return [vault.AbsolutePath("Objectives/Batch")];
			},
			snapshot => Assert.DoesNotContain("Incentive|", snapshot));

	[Fact]
	public async Task Moving_a_folder_of_begun_notes_into_an_ignored_folder_deletes_them_alike_at_startup_wakeup_and_runtime()
		=> await AssertParityAsync(
			SeedBegunObjectiveInFolderAsync,
			vault => Move(vault, "Objectives/Batch", ".trash/Batch", directory: true),
			snapshot => Assert.DoesNotContain("Incentive|", snapshot));

	[Fact]
	public async Task Deleting_a_directives_note_deletes_the_directive_alike_at_startup_wakeup_and_runtime()
		=> await AssertParityAsync(
			vault => SeedDirectiveAsync(vault),
			vault =>
			{
				File.Delete(vault.AbsolutePath(DirectiveRelativePath));
				return [vault.AbsolutePath(DirectiveRelativePath)];
			},
			snapshot => Assert.DoesNotContain("Directive|", snapshot));

	[Fact]
	public async Task Deleting_a_directives_folder_as_one_event_deletes_the_directive_alike_at_startup_wakeup_and_runtime()
		=> await AssertParityAsync(
			vault => SeedDirectiveAsync(vault),
			vault =>
			{
				Directory.Delete(vault.AbsolutePath(DirectiveFolder), recursive: true);
				return [vault.AbsolutePath(DirectiveFolder)];
			},
			snapshot => Assert.DoesNotContain("Directive|", snapshot));

	[Fact]
	public async Task Removing_a_directive_notes_identity_deletes_the_directive_alike_at_startup_wakeup_and_runtime()
		=> await AssertParityAsync(
			vault => SeedDirectiveAsync(vault),
			vault => StripIdentity(vault, DirectiveRelativePath),
			snapshot =>
			{
				Assert.DoesNotContain("Directive|", snapshot);
				Assert.Contains("FILE|Directives/Realm/Realm.md::", snapshot);
			});

	[Fact]
	public async Task Moving_a_directives_folder_keeps_the_directive_alike_at_startup_wakeup_and_runtime()
		=> await AssertParityAsync(
			vault => SeedDirectiveAsync(vault),
			vault => Move(vault, DirectiveFolder, "Directives/Archive/Realm", directory: true),
			snapshot => Assert.Contains("Directive|<ID0>|Realm", snapshot));

	[Fact]
	public async Task Deleting_a_directives_folder_deletes_it_and_its_begun_objective_alike_at_startup_wakeup_and_runtime()
		=> await AssertParityAsync(
			vault => SeedDirectiveWithObjectiveAsync(vault, begun: true),
			vault =>
			{
				Directory.Delete(vault.AbsolutePath(DirectiveFolder), recursive: true);
				return [vault.AbsolutePath(DirectiveFolder)];
			},
			snapshot =>
			{
				Assert.DoesNotContain("Incentive|", snapshot);
				Assert.DoesNotContain("Directive|", snapshot);
			});

	[Fact]
	public async Task A_directive_whose_note_is_gone_but_still_holds_objectives_stays_alike_at_startup_wakeup_and_runtime()
		=> await AssertParityAsync(
			// The objective was never begun: it has no note, lives only in the database, and still names the directive, so
			// the directive's delete is refused (as the API refuses it) — the same way on every path.
			vault => SeedDirectiveWithObjectiveAsync(vault, begun: false),
			vault =>
			{
				File.Delete(vault.AbsolutePath(DirectiveRelativePath));
				return [vault.AbsolutePath(DirectiveRelativePath)];
			},
			snapshot =>
			{
				Assert.Contains("|Realm", snapshot);
				Assert.Contains("|Quest", snapshot);
			});
}
