using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Saga;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Markdown;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The filename character filter (PEP097) applied at the markdown read/write chokepoints: a title character that would
/// break the filesystem or Obsidian indexing is written into the filename as a reversible look-alike and restored when
/// the filename is read or scanned. These exercise the three operations end to end — writing a path, reading identity
/// back from a path, and the live scan/reconcile round-trip — through the real composer, locator, and watcher pipeline,
/// so that the codec sitting at the chokepoints is enough for a forbidden-character title to survive intact.
/// </summary>
public sealed class FilenameConstraintTests : VaultTestBase
{
	private MarkdownFileLocator Locator => Vault.GetSingleton<MarkdownFileLocator>();
	private VaultStoragePathComposer Composer => Vault.GetSingleton<VaultStoragePathComposer>();

	/// <summary>Vault-relative path with forward slashes for stable golden assertions (real look-alikes kept literal).</summary>
	private string Relative(string absolutePath)
		=> Path.GetRelativePath(Vault.VaultRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');

	// --- write: title -> filename (encode) ---

	[Fact]
	public void Quiet_title_only_filename_encodes_forbidden_characters()
	{
		// A quiet objective's whole filename is its title; every forbidden character becomes its look-alike.
		var objective = new Objective { Id = "j12345678", Title = "A/B: C?" };

		var path = Locator.GetObjectiveFilePath(objective);

		Assert.Equal("Objectives/A∕Bː C？.md", Relative(path));
	}

	[Fact]
	public void Index_filename_encodes_the_title_but_leaves_the_puck_token_and_separator_intact()
	{
		// An index cycle's filename is "{token} - {title}"; only the title portion carries forbidden characters, and the
		// " - " separator (no forbidden character) is untouched, so the name still parses back into token and title.
		var cycle = new PolarisCycle { Id = "20260101", Title = "New: Year?" };

		var path = Locator.GetPolarisCycleFilePath(cycle);

		Assert.Equal("Journal/20260101 - Newː Year？.md", Relative(path));
	}

	[Fact]
	public void Self_named_directory_encodes_both_the_folder_and_the_file()
	{
		// A directive owns a self-named folder holding a same-named file; both segments encode the forbidden character.
		var directive = new StellarDirective { Id = "A12345678", Title = "Plan|X" };

		Assert.Equal("Directives/Plan⏐X/Plan⏐X.md", Relative(Locator.GetDirectiveFilePath(directive)));
		Assert.Equal("Directives/Plan⏐X", Relative(Locator.GetDirectiveDirectoryPath(directive)));
	}

	[Fact]
	public void Obsidian_reserved_characters_are_encoded_too()
	{
		// The Obsidian-only set ([ ] ^ #) is filesystem-legal but disables indexing, so it is encoded alongside the
		// filesystem-forbidden set.
		var objective = new Objective { Id = "j12345678", Title = "[A] ^B #C" };

		Assert.Equal("Objectives/⦋A⦌ ˆB ♯C.md", Relative(Locator.GetObjectiveFilePath(objective)));
	}

	// --- read: filename -> identity (decode) ---

	[Fact]
	public void Reverse_composition_recovers_a_forbidden_character_title_from_a_quiet_path()
	{
		var objective = new Objective { Id = "j12345678", Title = string.Empty };

		Composer.ApplyCompositionFromPath(objective, Vault.AbsolutePath("Objectives/A∕Bː C？.md"));

		Assert.Equal("A/B: C?", objective.Title);
	}

	[Fact]
	public void Reverse_composition_recovers_the_token_and_a_forbidden_character_title_from_an_index_path()
	{
		var cycle = new PolarisCycle { Id = string.Empty, Title = string.Empty };

		Composer.ApplyCompositionFromPath(cycle, Vault.AbsolutePath("Journal/20260101 - Newː Year？.md"));

		Assert.Equal("20260101", cycle.Id);
		Assert.Equal("New: Year?", cycle.Title);
	}

	[Fact]
	public void Loose_path_identity_parsing_decodes_the_title()
	{
		var (id, title) = MarkdownFileLocator.ParseLoosePuckIdentityFromPath(
			Vault.AbsolutePath("Journal/20260101 - Newː Year？.md"));

		Assert.Equal("20260101", id);
		Assert.Equal("New: Year?", title);
	}

	[Fact]
	public void Read_filename_identity_is_the_exact_inverse_of_the_written_base_name()
	{
		// The chokepoint pair round-trips: compose a path for a nasty title, then read the identity back from that path.
		var written = new Objective { Id = "j12345678", Title = "Q4: plan <v2>? #done" };
		var path = Locator.GetObjectiveFilePath(written);

		var (_, title) = Composer.ReadFilenameIdentity(typeof(Objective), path);

		Assert.Equal(written.Title, title);
	}

	// --- scan: the live pipeline reads an encoded file back to the real title ---

	[Fact]
	public async Task Materializing_then_reconciling_a_forbidden_character_title_round_trips_without_churn()
	{
		// Write: materializing the quiet file composes the encoded on-disk name.
		const string title = "Review: Q4 #plan?";
		var objective = await Vault.SeedStandaloneObjectiveAsync(title, "j00000abc");
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);

		Assert.True(
			Vault.VaultFileExists("Objectives/Reviewː Q4 ♯plan？.md"),
			"materialization must encode the forbidden characters in the filename");

		// Scan/read: the watcher observes its own file; decoding at the read chokepoint recovers the real title, so the
		// stored title is unchanged and the file is neither renamed nor purged (a missing decode would look like an edit).
		await Vault.ReconcileAsync(Vault.AbsolutePath("Objectives/Reviewː Q4 ♯plan？.md"));

		var stored = await Vault.QueryAsync(context => context.Incentives.AsNoTracking()
			.OfType<Objective>().SingleAsync(item => item.Id == objective.Id, TestContext.Current.CancellationToken));
		Assert.Equal(title, stored.Title);
		Assert.True(Vault.VaultFileExists("Objectives/Reviewː Q4 ♯plan？.md"), "the encoded file must survive reconcile");
	}

	[Fact]
	public async Task A_startup_scan_derives_the_real_title_from_an_encoded_filename()
	{
		// A pre-existing encoded file on disk (as Obsidian would hold it): the discovery scan must read its title decoded.
		Vault.WriteVaultFile(
			"Objectives/Ship ∕2ː v？.md",
			"---\n" + "puck: j00000def\n" + "---\n");

		var candidate = await Vault.InspectAsync(Vault.AbsolutePath("Objectives/Ship ∕2ː v？.md"));

		Assert.NotNull(candidate);
		Assert.Equal("Ship /2: v?", candidate!.PathTitle);
	}
}
