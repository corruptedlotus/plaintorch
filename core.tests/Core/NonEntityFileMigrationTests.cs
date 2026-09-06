using Microsoft.EntityFrameworkCore;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Non-entity file isolation across a full daemon restart: files that pre-exist on disk are observed by the
/// startup <em>migrations</em> (which run inside <c>InitializeVaultAsync</c> before the watcher) as well as the
/// consistency pass and discovery sweep. A non-entity note — outside every entity root and entity directory, no
/// frontmatter PUCK, not in the database — must survive the whole initialization intact, including a filename
/// prefix that happens to be PUCK-shaped. The migration loader is confined to declared territory exactly like the
/// watcher: a filename-embedded identity is trusted only inside a specific scan root or a database-known hosting
/// parent's folder, never on the strength of a PUCK-shaped prefix alone.
/// </summary>
/// <remarks>
/// These tests place files before <see cref="TestVault.InitializeAsync"/> so the real v1→v2 objective migration
/// (which reads objectives under the legacy Index filename convention) runs over them — the path that stripped a
/// PUCK-shaped prefix from a non-owned note before the territory confinement fix.
/// </remarks>
public sealed class NonEntityFileMigrationTests : IDisposable
{
	private readonly TestVault _vault = new();

	private async Task InitializeWithAsync(params (string RelativePath, string Content)[] files)
	{
		_vault.PreExistingFiles = files;
		await _vault.InitializeAsync();
	}

	private async Task AssertUntouchedAndNoEntitiesAsync(string vaultRelativePath, string content)
	{
		Assert.True(_vault.VaultFileExists(vaultRelativePath), $"File '{vaultRelativePath}' should have survived initialization.");
		Assert.Equal(content, _vault.ReadVaultFile(vaultRelativePath));
		var incentives = await _vault.QueryAsync(context => context.Incentives.CountAsync(TestContext.Current.CancellationToken));
		var directives = await _vault.QueryAsync(context => context.Directives.CountAsync(TestContext.Current.CancellationToken));
		Assert.Equal(0, incentives);
		Assert.Equal(0, directives);
	}

	private static string NoteBody(string heading)
		=> $"# {heading}" + Environment.NewLine + "body" + Environment.NewLine;

	[Fact]
	public async Task A_word_prefixed_note_in_a_plain_folder_survives_initialization()
	{
		var content = NoteBody("Definitions");
		await InitializeWithAsync(("Stuff/Project - Definitions and Stuff.md", content));

		await AssertUntouchedAndNoEntitiesAsync("Stuff/Project - Definitions and Stuff.md", content);
	}

	[Fact]
	public async Task A_puck_shaped_note_in_a_plain_folder_survives_initialization()
	{
		// The prefix tokenizes as a genuine objective PUCK, but the file is in a non-owned folder and is not in the
		// database — so the migration must not adopt it as a legacy objective and strip its prefix.
		var content = NoteBody("Draft");
		await InitializeWithAsync(("Projects/j00000099 - Draft.md", content));

		await AssertUntouchedAndNoEntitiesAsync("Projects/j00000099 - Draft.md", content);
	}

	[Fact]
	public async Task A_puck_shaped_note_at_the_vault_root_survives_initialization()
	{
		var content = NoteBody("Draft");
		await InitializeWithAsync(("j00000099 - Draft.md", content));

		await AssertUntouchedAndNoEntitiesAsync("j00000099 - Draft.md", content);
	}

	[Fact]
	public async Task A_puck_shaped_note_in_a_nested_subfolder_survives_initialization()
	{
		var content = NoteBody("Entry");
		await InitializeWithAsync(("Deep/Nested/Folder/j00000099 - Entry.md", content));

		await AssertUntouchedAndNoEntitiesAsync("Deep/Nested/Folder/j00000099 - Entry.md", content);
	}

	[Fact]
	public async Task A_date_prefixed_note_in_a_plain_folder_survives_initialization()
	{
		var content = NoteBody("Roadmap");
		await InitializeWithAsync(("Projects/2024-01 - Roadmap.md", content));

		await AssertUntouchedAndNoEntitiesAsync("Projects/2024-01 - Roadmap.md", content);
	}

	[Fact]
	public async Task A_plain_note_in_a_plain_folder_survives_initialization()
	{
		var content = NoteBody("Roadmap Draft");
		await InitializeWithAsync(("Projects/Roadmap Draft.md", content));

		await AssertUntouchedAndNoEntitiesAsync("Projects/Roadmap Draft.md", content);
	}

	public void Dispose() => _vault.DisposeAsync().AsTask().GetAwaiter().GetResult();
}
