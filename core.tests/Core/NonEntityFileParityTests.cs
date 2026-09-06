using Microsoft.EntityFrameworkCore;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Startup/runtime parity for non-entity files: a set of files that are not the watcher's authority (outside
/// every entity root and entity directory, no frontmatter PUCK, not in the database) must reach the same final
/// state whether the daemon boots over them (a startup sweep) or observes them as live filesystem events (a
/// runtime scan). Any divergence — a renamed, stripped, moved, or deleted note — means the startup pipeline
/// treats the user's own files more aggressively than the live watcher does, which is exactly the class of bug
/// these tests pin (see <c>core/Vault/Watcher/.GENESIS.md</c>: startup and runtime agree).
/// </summary>
public sealed class NonEntityFileParityTests
{
	/// <summary>
	/// Arranges the same files in two fresh vaults: the first is reconciled by a startup sweep, the second by a
	/// sequence of runtime events (one per affected path), and the two final snapshots are returned for comparison.
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
			await sweepVault.StartupSweepAsync();

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
		var entityCount = await vault.QueryAsync(async context =>
			await context.Incentives.CountAsync(TestContext.Current.CancellationToken)
			+ await context.Directives.CountAsync(TestContext.Current.CancellationToken));

		var fileRows = Directory.EnumerateFiles(vault.VaultRoot, "*", SearchOption.AllDirectories)
			.Where(path => !path.Contains(".plaintorch", StringComparison.OrdinalIgnoreCase))
			.Select(path => $"FILE|{Path.GetRelativePath(vault.VaultRoot, path).Replace('\\', '/')}::{File.ReadAllText(path)}");

		return $"ENTITIES|{entityCount}\n" + string.Join("\n", fileRows.OrderBy(row => row, StringComparer.Ordinal));
	}

	private static string NoteBody(string heading)
		=> $"# {heading}" + Environment.NewLine + "body" + Environment.NewLine;

	[Fact]
	public async Task Plain_non_entity_notes_reach_the_same_state_at_startup_and_runtime()
	{
		var (sweep, runtime) = await RunBothWaysAsync(vault =>
		{
			vault.WriteVaultFile("Fleeting Thoughts.md", NoteBody("Fleeting Thoughts"));
			vault.WriteVaultFile("Projects/Roadmap Draft.md", NoteBody("Roadmap Draft"));
			vault.WriteVaultFile("Projects/Archive/2025/Meeting Notes.md", NoteBody("Meeting Notes"));
			return Task.FromResult<IReadOnlyList<string>>(
			[
				vault.AbsolutePath("Fleeting Thoughts.md"),
				vault.AbsolutePath("Projects/Roadmap Draft.md"),
				vault.AbsolutePath("Projects/Archive/2025/Meeting Notes.md"),
			]);
		});

		Assert.Equal(sweep, runtime);
	}

	[Fact]
	public async Task Index_like_notes_reach_the_same_state_at_startup_and_runtime()
	{
		var (sweep, runtime) = await RunBothWaysAsync(vault =>
		{
			vault.WriteVaultFile("2024-01 - Roadmap.md", NoteBody("Roadmap"));
			vault.WriteVaultFile("Projects/2024-01 - Roadmap.md", NoteBody("Roadmap"));
			vault.WriteVaultFile("Projects/Archive/2025/001 - Meeting Notes.md", NoteBody("Meeting Notes"));
			vault.WriteVaultFile("Projects/j00000099 - Draft Task.md", NoteBody("Draft Task"));
			return Task.FromResult<IReadOnlyList<string>>(
			[
				vault.AbsolutePath("2024-01 - Roadmap.md"),
				vault.AbsolutePath("Projects/2024-01 - Roadmap.md"),
				vault.AbsolutePath("Projects/Archive/2025/001 - Meeting Notes.md"),
				vault.AbsolutePath("Projects/j00000099 - Draft Task.md"),
			]);
		});

		Assert.Equal(sweep, runtime);
	}

	[Fact]
	public async Task Non_markdown_files_reach_the_same_state_at_startup_and_runtime()
	{
		var (sweep, runtime) = await RunBothWaysAsync(vault =>
		{
			vault.WriteVaultFile("notes.txt", "plain text, not markdown" + Environment.NewLine);
			vault.WriteVaultFile("Projects/Views.base", "obsidian base" + Environment.NewLine);
			vault.WriteVaultFile("Projects/Archive/assets/logo.png", "\x89PNG\r\n\x1a\nfake-image-bytes");
			return Task.FromResult<IReadOnlyList<string>>(
			[
				vault.AbsolutePath("notes.txt"),
				vault.AbsolutePath("Projects/Views.base"),
				vault.AbsolutePath("Projects/Archive/assets/logo.png"),
			]);
		});

		Assert.Equal(sweep, runtime);
	}
}
