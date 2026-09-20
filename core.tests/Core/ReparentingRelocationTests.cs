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
/// Reparenting is a move, not a rewrite: when an entity gains, changes, or loses a parent, its note must travel to the
/// one place its new parentage implies — old note gone, one note at the destination, its body and unknown frontmatter
/// carried over intact — and a brand-new child must be born beneath its parent's <em>actual</em> folder even when that
/// parent lives outside the entity's default root.
///
/// The existing <see cref="DirectiveReparentingTests"/> pin the canonical, in-root cases (a note follows on disk). These
/// add the two gaps the reported misbehaviour lives in: (1) content carried across the move (asserted, not just
/// existence), and (2) placement when the parent is out of the default root — where the write path composes the child
/// under the parent's <em>canonical</em> folder (<c>VaultStoragePathComposer.GetOwnDirectory</c> recurses declared
/// storage, never the parent's real location), while discovery resolves the parent by where its file actually is.
///
/// Several of these are expected to fail on dev/phase2d; they are the acceptance criteria for making write-side
/// placement resolve the parent's real location the same way read-side discovery does.
/// </summary>
public sealed class ReparentingRelocationTests : VaultTestBase
{
	private const string BodyMarker = "Carry me across the move.";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private Task<StellarDirective> CreateDirectiveAsync(string title)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync(title, cancellationToken: Token));

	private Task<StellarDirective> CreateChildDirectiveAsync(string parentId, string title)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.CreateFromParentAsync(parentId, title, cancellationToken: Token));

	private Task<Directive> InitDirectiveAtAsync(string noteRelativePath)
	{
		Vault.WriteVaultFile(noteRelativePath, "# Directive" + Environment.NewLine + BodyMarker + Environment.NewLine);
		return Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.InitializeFromPathAsync(noteRelativePath, Token));
	}

	private Task<Objective> CreateObjectiveUnderAsync(string directiveId, string title)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.CreateFromDirectiveAsync(directiveId, title, cancellationToken: Token));

	private Task<Objective> UpdateObjectiveAsync(string id, ObjectiveUpdate update)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.UpdateAsync(id, update, Token));

	private Task<StellarDirective> UpdateStellarAsync(string id, StellarDirectiveUpdate update)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.UpdateStellarAsync(id, update, Token));

	/// <summary>Every markdown file in the vault (outside metadata) that carries the given PUCK.</summary>
	private string[] NotesAssertingPuck(string puck)
	{
		return Vault.MarkdownFilesUnder(Vault.VaultRoot)
			.Where(path => !path.StartsWith(Vault.Layout.MetadataRoot, StringComparison.OrdinalIgnoreCase))
			.Where(path => File.ReadAllText(path).Contains($"puck: {puck}", StringComparison.Ordinal))
			.ToArray();
	}

	private static void AppendToFile(string absolutePath, string text)
		=> File.AppendAllText(absolutePath, text + Environment.NewLine);

	// --- Scenario 1: reparenting moves the note and carries its content ---

	[Fact]
	public async Task Reparenting_a_directive_moves_the_folder_and_carries_its_body_and_unknown_frontmatter()
	{
		var campaign = await CreateDirectiveAsync("Campaign");
		var strike = await CreateDirectiveAsync("Strike");
		// The user adds their own content and their own frontmatter key to the directive note.
		var strikeNote = Vault.AbsolutePath("Directives/Strike/Strike.md");
		var withKey = File.ReadAllText(strikeNote).Replace("puck:", "keepme: yes" + Environment.NewLine + "puck:", StringComparison.Ordinal);
		File.WriteAllText(strikeNote, withKey + Environment.NewLine + BodyMarker + Environment.NewLine);

		await UpdateStellarAsync(strike.Id, new StellarDirectiveUpdate(ParentDirectiveId: campaign.Id));

		var notes = NotesAssertingPuck(strike.Id);
		Assert.Single(notes);
		Assert.True(Vault.VaultFileExists("Directives/Campaign/Strike/Strike.md"), "the note must live under its new parent");
		Assert.False(Directory.Exists(Vault.AbsolutePath("Directives/Strike")), "the old folder must not linger");
		var moved = Vault.ReadVaultFile("Directives/Campaign/Strike/Strike.md");
		Assert.Contains(BodyMarker, moved, StringComparison.Ordinal);
		Assert.Contains("keepme: yes", moved, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Reparenting_an_objective_moves_its_note_and_carries_its_body()
	{
		var alpha = await CreateDirectiveAsync("Alpha");
		var beta = await CreateDirectiveAsync("Beta");
		var objective = await CreateObjectiveUnderAsync(alpha.Id, "Take the bridge");
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);
		var note = NotesAssertingPuck(objective.Id).Single();
		AppendToFile(note, BodyMarker);

		await UpdateObjectiveAsync(objective.Id, new ObjectiveUpdate(DirectiveId: beta.Id));

		var moved = NotesAssertingPuck(objective.Id);
		Assert.Single(moved);
		Assert.StartsWith(Vault.AbsolutePath("Directives/Beta"), moved[0], StringComparison.OrdinalIgnoreCase);
		Assert.Contains(BodyMarker, File.ReadAllText(moved[0]), StringComparison.Ordinal);
	}

	[Fact]
	public async Task Reparenting_an_objective_to_standalone_moves_its_note_to_the_objectives_root_carrying_its_body()
	{
		var alpha = await CreateDirectiveAsync("Alpha");
		var objective = await CreateObjectiveUnderAsync(alpha.Id, "Take the bridge");
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);
		AppendToFile(NotesAssertingPuck(objective.Id).Single(), BodyMarker);

		await UpdateObjectiveAsync(objective.Id, new ObjectiveUpdate(DirectiveId: new Optional<string?>(null)));

		var moved = NotesAssertingPuck(objective.Id);
		Assert.Single(moved);
		Assert.StartsWith(Vault.Layout.ObjectivesRoot, moved[0], StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain(NotesAssertingPuck(objective.Id), path => path.Contains($"Directives{Path.DirectorySeparatorChar}Alpha", StringComparison.OrdinalIgnoreCase));
		Assert.Contains(BodyMarker, File.ReadAllText(moved[0]), StringComparison.Ordinal);
	}

	[Fact]
	public async Task Reparenting_a_dashed_title_objective_moves_cleanly_without_duplicating()
	{
		var alpha = await CreateDirectiveAsync("Alpha");
		var beta = await CreateDirectiveAsync("Beta");
		var objective = await CreateObjectiveUnderAsync(alpha.Id, "Q1 - Ship it");
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);
		Assert.True(Vault.VaultFileExists("Directives/Alpha/Objectives/Q1 - Ship it.md"), "precondition: dashed title kept whole");

		await UpdateObjectiveAsync(objective.Id, new ObjectiveUpdate(DirectiveId: beta.Id));

		var moved = NotesAssertingPuck(objective.Id);
		Assert.Single(moved);
		Assert.True(Vault.VaultFileExists("Directives/Beta/Objectives/Q1 - Ship it.md"));
		Assert.False(Vault.VaultFileExists("Directives/Alpha/Objectives/Q1 - Ship it.md"));
	}

	// --- Scenario 2: a parent outside the default root hosts its child in its real folder ---

	[Fact(Skip = "CONFIRMED BUG (dev/phase2d; acceptance criterion for the write/read-convergence work). Reproduced: a directive kept at Projects/Campaign (freeform, outside the Directives root) hosts a NEW objective, which materializes at Directives/Campaign/Objectives/... — the parent's canonical folder, freshly conjured — instead of Projects/Campaign/Objectives. Root cause: a brand-new child has no existing file to locate by identity, so PlaintorchMarkdownStorageService composes the canonical path via VaultStoragePathComposer.GetContainer -> GetOwnDirectory, which recurses the parent's DECLARED storage (canonical location), never the parent's actual on-disk location; discovery instead resolves the parent by where its file really is (ResolveFreeformDirectiveParentIdAsync). Write-side placement diverges from read-side discovery. Contrast: editing/renaming an EXISTING out-of-root objective works (found by identity). Fix steer: placement must resolve the parent's real path the way discovery does — one owner for the entity<->file mapping.")]
	public async Task A_new_objective_under_an_out_of_root_directive_materializes_in_the_parents_real_folder()
	{
		// A freeform directive the user keeps outside the Directives root.
		var directive = await InitDirectiveAtAsync("Projects/Campaign/Campaign.md");

		var objective = await CreateObjectiveUnderAsync(directive.Id, "Take the bridge");
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);

		var notes = NotesAssertingPuck(objective.Id);
		Assert.Single(notes);
		// It must be born under the parent's ACTUAL folder, not a freshly-minted canonical Directives/Campaign, and not
		// the default Objectives root.
		Assert.True(notes[0].StartsWith(Vault.AbsolutePath("Projects/Campaign"), StringComparison.OrdinalIgnoreCase),
			$"a new objective should be born under its parent's real folder Projects/Campaign, but was placed at '{Path.GetRelativePath(Vault.VaultRoot, notes[0])}'");
		Assert.False(Directory.Exists(Vault.AbsolutePath("Directives/Campaign")), "the parent's canonical folder must not be conjured");
	}

	[Fact(Skip = "CONFIRMED BUG (dev/phase2d; acceptance criterion for the write/read-convergence work). Reproduced: reparenting a standalone objective INTO a directive kept at Projects/Campaign moves its note to Directives/Campaign/Objectives/... (the parent's conjured canonical folder), not Projects/Campaign. Root cause: PlaintorchMarkdownStorageService.SaveCanonicalMarkdownAsync forces newPath = canonicalPath whenever IsReparented is true, and canonicalPath is composed against the parent's DECLARED (canonical) directory rather than its actual out-of-root location. Fix steer: reparent placement must resolve the parent's real folder — same resolver on write as on read.")]
	public async Task Reparenting_an_objective_into_an_out_of_root_directive_moves_its_note_under_that_parent()
	{
		var directive = await InitDirectiveAtAsync("Projects/Campaign/Campaign.md");
		var objective = await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.CreateStandaloneAsync("Take the bridge", cancellationToken: Token));
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);
		Assert.StartsWith(Vault.Layout.ObjectivesRoot, NotesAssertingPuck(objective.Id).Single(), StringComparison.OrdinalIgnoreCase);

		await UpdateObjectiveAsync(objective.Id, new ObjectiveUpdate(DirectiveId: directive.Id));

		var moved = NotesAssertingPuck(objective.Id);
		Assert.Single(moved);
		Assert.True(moved[0].StartsWith(Vault.AbsolutePath("Projects/Campaign"), StringComparison.OrdinalIgnoreCase),
			$"the reparented objective should move under its new parent's real folder Projects/Campaign, but was at '{Path.GetRelativePath(Vault.VaultRoot, moved[0])}'");
		Assert.False(Directory.Exists(Vault.AbsolutePath("Directives/Campaign")), "the parent's canonical folder must not be conjured");
	}

	[Fact(Skip = "CONFIRMED BUG (dev/phase2d; acceptance criterion for the write/read-convergence work). Reproduced: a child directive created under a parent directive kept at Projects/Campaign materializes at Directives/Campaign/Strike/Strike.md, not Projects/Campaign/Strike. Root cause: same as the objective case — VaultStoragePathComposer.GetOwnDirectory composes the parent's canonical directory, so a new child is placed under a conjured Directives/Campaign rather than the parent's real folder. Fix steer: compose a child against the parent's resolved actual location.")]
	public async Task A_new_child_directive_under_an_out_of_root_parent_materializes_beneath_it()
	{
		var parent = await InitDirectiveAtAsync("Projects/Campaign/Campaign.md");

		var child = await CreateChildDirectiveAsync(parent.Id, "Strike");

		var notes = NotesAssertingPuck(child.Id);
		Assert.Single(notes);
		Assert.True(Vault.VaultFileExists("Projects/Campaign/Strike/Strike.md"),
			$"the child directive should be born beneath its parent at Projects/Campaign/Strike, but its note is at '{Path.GetRelativePath(Vault.VaultRoot, notes[0])}'");
		Assert.False(Directory.Exists(Vault.AbsolutePath("Directives/Campaign")), "the parent's canonical folder must not be conjured");
	}

	// --- Golden control: in-root placement still lands in the canonical partition ---

	[Fact]
	public async Task A_new_objective_under_an_in_root_directive_materializes_in_its_partition()
	{
		var directive = await CreateDirectiveAsync("Campaign");

		var objective = await CreateObjectiveUnderAsync(directive.Id, "Take the bridge");
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);

		Assert.True(Vault.VaultFileExists("Directives/Campaign/Objectives/Take the bridge.md"));
		Assert.Single(NotesAssertingPuck(objective.Id));
	}

	[Fact]
	public async Task A_reparented_objectives_directive_relation_is_persisted()
	{
		var alpha = await CreateDirectiveAsync("Alpha");
		var beta = await CreateDirectiveAsync("Beta");
		var objective = await CreateObjectiveUnderAsync(alpha.Id, "Take the bridge");
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);

		await UpdateObjectiveAsync(objective.Id, new ObjectiveUpdate(DirectiveId: beta.Id));

		var stored = await Vault.QueryAsync(context => context.Incentives.AsNoTracking()
			.OfType<Objective>().SingleAsync(item => item.Id == objective.Id, Token));
		Assert.Equal(beta.Id, stored.DirectiveId);
	}
}
