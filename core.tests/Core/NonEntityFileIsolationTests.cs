using Microsoft.EntityFrameworkCore;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Non-entity file isolation: a file that lives outside every entity root and outside every entity directory,
/// carries no frontmatter PUCK identity, and is not registered in the database is <em>not the watcher's
/// authority</em>. The watcher must never reconcile it into an entity, rename it, strip a filename prefix, move
/// it, or delete it — neither at startup (the consistency pass + discovery sweep) nor at runtime (a live
/// filesystem event). These are the guarantees a user's own notes, assets, and Obsidian artefacts rely on to
/// coexist with managed vault content (see <c>core/Vault/Watcher/.GENESIS.md</c>: authority is confined to
/// declared territory, and outside auto-assign roots policy is PUCK-based, never filename/path-shaped).
/// </summary>
public sealed class NonEntityFileIsolationTests : VaultTestBase
{
	private Task<int> IncentiveCountAsync()
		=> Vault.QueryAsync(context => context.Incentives.CountAsync(TestContext.Current.CancellationToken));

	private Task<int> DirectiveCountAsync()
		=> Vault.QueryAsync(context => context.Directives.CountAsync(TestContext.Current.CancellationToken));

	private async Task AssertUntouchedAndNoEntitiesAsync(string vaultRelativePath, string content)
	{
		Assert.True(Vault.VaultFileExists(vaultRelativePath), $"File '{vaultRelativePath}' should have survived.");
		Assert.Equal(content, Vault.ReadVaultFile(vaultRelativePath));
		Assert.Equal(0, await IncentiveCountAsync());
		Assert.Equal(0, await DirectiveCountAsync());
	}

	private static string NoteBody(string heading)
		=> $"# {heading}" + Environment.NewLine + "body" + Environment.NewLine;

	// --- Group 1: plain non-entity markdown notes survive a startup sweep ---

	[Fact]
	public async Task A_plain_note_at_the_vault_root_survives_a_startup_sweep()
	{
		var content = NoteBody("Fleeting Thoughts");
		Vault.WriteVaultFile("Fleeting Thoughts.md", content);

		await Vault.StartupSweepAsync();

		await AssertUntouchedAndNoEntitiesAsync("Fleeting Thoughts.md", content);
	}

	[Fact]
	public async Task A_plain_note_in_a_plain_folder_survives_a_startup_sweep()
	{
		var content = NoteBody("Roadmap Draft");
		Vault.WriteVaultFile("Projects/Roadmap Draft.md", content);

		await Vault.StartupSweepAsync();

		await AssertUntouchedAndNoEntitiesAsync("Projects/Roadmap Draft.md", content);
	}

	[Fact]
	public async Task A_plain_note_in_a_nested_subfolder_survives_a_startup_sweep()
	{
		var content = NoteBody("Meeting Notes");
		Vault.WriteVaultFile("Projects/Archive/2025/Meeting Notes.md", content);

		await Vault.StartupSweepAsync();

		await AssertUntouchedAndNoEntitiesAsync("Projects/Archive/2025/Meeting Notes.md", content);
	}

	// --- Group 1a: plain non-entity markdown notes survive a runtime scan ---

	[Fact]
	public async Task A_plain_note_at_the_vault_root_is_not_a_candidate_and_survives_a_runtime_scan()
	{
		var content = NoteBody("Fleeting Thoughts");
		Vault.WriteVaultFile("Fleeting Thoughts.md", content);

		Assert.Null(await Vault.InspectAsync(Vault.AbsolutePath("Fleeting Thoughts.md")));
		await Vault.ReconcileAsync(Vault.AbsolutePath("Fleeting Thoughts.md"));

		await AssertUntouchedAndNoEntitiesAsync("Fleeting Thoughts.md", content);
	}

	[Fact]
	public async Task A_plain_note_in_a_plain_folder_is_not_a_candidate_and_survives_a_runtime_scan()
	{
		var content = NoteBody("Roadmap Draft");
		Vault.WriteVaultFile("Projects/Roadmap Draft.md", content);

		Assert.Null(await Vault.InspectAsync(Vault.AbsolutePath("Projects/Roadmap Draft.md")));
		await Vault.ReconcileAsync(Vault.AbsolutePath("Projects/Roadmap Draft.md"));

		await AssertUntouchedAndNoEntitiesAsync("Projects/Roadmap Draft.md", content);
	}

	[Fact]
	public async Task A_plain_note_in_a_nested_subfolder_is_not_a_candidate_and_survives_a_runtime_scan()
	{
		var content = NoteBody("Meeting Notes");
		Vault.WriteVaultFile("Projects/Archive/2025/Meeting Notes.md", content);

		Assert.Null(await Vault.InspectAsync(Vault.AbsolutePath("Projects/Archive/2025/Meeting Notes.md")));
		await Vault.ReconcileAsync(Vault.AbsolutePath("Projects/Archive/2025/Meeting Notes.md"));

		await AssertUntouchedAndNoEntitiesAsync("Projects/Archive/2025/Meeting Notes.md", content);
	}

	// --- Group 2: index-like "prefix - title" markdown notes survive a startup sweep ---

	[Fact]
	public async Task An_index_like_note_at_the_vault_root_survives_a_startup_sweep()
	{
		var content = NoteBody("Roadmap");
		Vault.WriteVaultFile("2024-01 - Roadmap.md", content);

		await Vault.StartupSweepAsync();

		await AssertUntouchedAndNoEntitiesAsync("2024-01 - Roadmap.md", content);
	}

	[Fact]
	public async Task An_index_like_note_in_a_plain_folder_survives_a_startup_sweep()
	{
		var content = NoteBody("Roadmap");
		Vault.WriteVaultFile("Projects/2024-01 - Roadmap.md", content);

		await Vault.StartupSweepAsync();

		await AssertUntouchedAndNoEntitiesAsync("Projects/2024-01 - Roadmap.md", content);
	}

	[Fact]
	public async Task An_index_like_note_in_a_nested_subfolder_survives_a_startup_sweep()
	{
		var content = NoteBody("Meeting Notes");
		Vault.WriteVaultFile("Projects/Archive/2025/001 - Meeting Notes.md", content);

		await Vault.StartupSweepAsync();

		await AssertUntouchedAndNoEntitiesAsync("Projects/Archive/2025/001 - Meeting Notes.md", content);
	}

	[Fact]
	public async Task A_puck_shaped_note_in_a_plain_folder_survives_a_startup_sweep()
	{
		// The filename prefix tokenizes as a genuine objective PUCK, but the file is in a non-owned folder and is
		// not in the database — so it is a non-entity note, not a desynced entity file, and must be left alone.
		var content = NoteBody("Draft Task");
		Vault.WriteVaultFile("Projects/j00000099 - Draft Task.md", content);

		await Vault.StartupSweepAsync();

		await AssertUntouchedAndNoEntitiesAsync("Projects/j00000099 - Draft Task.md", content);
	}

	// --- Group 2a: index-like "prefix - title" markdown notes survive a runtime scan ---

	[Fact]
	public async Task An_index_like_note_at_the_vault_root_is_not_a_candidate_and_survives_a_runtime_scan()
	{
		var content = NoteBody("Roadmap");
		Vault.WriteVaultFile("2024-01 - Roadmap.md", content);

		Assert.Null(await Vault.InspectAsync(Vault.AbsolutePath("2024-01 - Roadmap.md")));
		await Vault.ReconcileAsync(Vault.AbsolutePath("2024-01 - Roadmap.md"));

		await AssertUntouchedAndNoEntitiesAsync("2024-01 - Roadmap.md", content);
	}

	[Fact]
	public async Task An_index_like_note_in_a_plain_folder_is_not_a_candidate_and_survives_a_runtime_scan()
	{
		var content = NoteBody("Roadmap");
		Vault.WriteVaultFile("Projects/2024-01 - Roadmap.md", content);

		Assert.Null(await Vault.InspectAsync(Vault.AbsolutePath("Projects/2024-01 - Roadmap.md")));
		await Vault.ReconcileAsync(Vault.AbsolutePath("Projects/2024-01 - Roadmap.md"));

		await AssertUntouchedAndNoEntitiesAsync("Projects/2024-01 - Roadmap.md", content);
	}

	[Fact]
	public async Task An_index_like_note_in_a_nested_subfolder_is_not_a_candidate_and_survives_a_runtime_scan()
	{
		var content = NoteBody("Meeting Notes");
		Vault.WriteVaultFile("Projects/Archive/2025/001 - Meeting Notes.md", content);

		Assert.Null(await Vault.InspectAsync(Vault.AbsolutePath("Projects/Archive/2025/001 - Meeting Notes.md")));
		await Vault.ReconcileAsync(Vault.AbsolutePath("Projects/Archive/2025/001 - Meeting Notes.md"));

		await AssertUntouchedAndNoEntitiesAsync("Projects/Archive/2025/001 - Meeting Notes.md", content);
	}

	[Fact]
	public async Task A_puck_shaped_note_in_a_plain_folder_is_not_a_candidate_and_survives_a_runtime_scan()
	{
		var content = NoteBody("Draft Task");
		Vault.WriteVaultFile("Projects/j00000099 - Draft Task.md", content);

		Assert.Null(await Vault.InspectAsync(Vault.AbsolutePath("Projects/j00000099 - Draft Task.md")));
		await Vault.ReconcileAsync(Vault.AbsolutePath("Projects/j00000099 - Draft Task.md"));

		await AssertUntouchedAndNoEntitiesAsync("Projects/j00000099 - Draft Task.md", content);
	}

	// --- Group 3: non-markdown files survive a startup sweep ---

	[Fact]
	public async Task A_non_markdown_file_at_the_vault_root_survives_a_startup_sweep()
	{
		var content = "plain text, not markdown" + Environment.NewLine;
		Vault.WriteVaultFile("notes.txt", content);

		await Vault.StartupSweepAsync();

		await AssertUntouchedAndNoEntitiesAsync("notes.txt", content);
	}

	[Fact]
	public async Task A_non_markdown_file_in_a_plain_folder_survives_a_startup_sweep()
	{
		var content = "obsidian base" + Environment.NewLine;
		Vault.WriteVaultFile("Projects/Views.base", content);

		await Vault.StartupSweepAsync();

		await AssertUntouchedAndNoEntitiesAsync("Projects/Views.base", content);
	}

	[Fact]
	public async Task A_binary_file_in_a_nested_subfolder_survives_a_startup_sweep()
	{
		Vault.WriteVaultFile("Projects/Archive/assets/logo.png", "\x89PNG\r\n\x1a\nfake-image-bytes");

		await Vault.StartupSweepAsync();

		Assert.True(Vault.VaultFileExists("Projects/Archive/assets/logo.png"));
		Assert.Equal(0, await IncentiveCountAsync());
		Assert.Equal(0, await DirectiveCountAsync());
	}

	// --- Group 3a: non-markdown files survive a runtime scan ---

	[Fact]
	public async Task A_non_markdown_file_in_a_plain_folder_is_not_a_candidate_and_survives_a_runtime_scan()
	{
		var content = "obsidian base" + Environment.NewLine;
		Vault.WriteVaultFile("Projects/Views.base", content);

		Assert.Null(await Vault.InspectAsync(Vault.AbsolutePath("Projects/Views.base")));
		await Vault.ReconcileAsync(Vault.AbsolutePath("Projects/Views.base"));

		await AssertUntouchedAndNoEntitiesAsync("Projects/Views.base", content);
	}

	[Fact]
	public async Task A_binary_file_in_a_nested_subfolder_is_not_a_candidate_and_survives_a_runtime_scan()
	{
		Vault.WriteVaultFile("Projects/Archive/assets/logo.png", "\x89PNG\r\n\x1a\nfake-image-bytes");

		Assert.Null(await Vault.InspectAsync(Vault.AbsolutePath("Projects/Archive/assets/logo.png")));
		await Vault.ReconcileAsync(Vault.AbsolutePath("Projects/Archive/assets/logo.png"));

		Assert.True(Vault.VaultFileExists("Projects/Archive/assets/logo.png"));
		Assert.Equal(0, await IncentiveCountAsync());
		Assert.Equal(0, await DirectiveCountAsync());
	}
}
