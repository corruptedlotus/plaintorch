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
/// A freeform/implicit note whose file lives outside the default root — hosted by a directive the user keeps outside the
/// Directives root, and/or sitting outside its parent's canonical partition — is still the entity's one real note.
/// Three obligations follow, each tested here:
///  (3) it must <em>resolve</em> back to its origin entity by its own asserted identity, wherever it sits;
///  (4) editing the entity through the API (a rename, or a mapped frontmatter field) must rewrite that one real file in
///      place — never spawn a second copy at the canonical location; and
///  (5) editing that file's frontmatter and letting the watcher read it back must sync the new values onto the core.
///
/// <see cref="FreeformNoteResolutionTests"/> and <see cref="DirectiveOutOfRootDuplicationTests"/> pin the directive
/// (freeform) side of (3)/(4) at the canonical shape; these carry it onto implicit objectives and out-of-root parents,
/// where write-side placement resolves the parent canonically and the identity-location scan may not reach the note.
/// Some are expected to fail on dev/phase2d and stand as acceptance criteria.
/// </summary>
public sealed class FreeformOutOfRootEditTests : VaultTestBase
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private Task<StellarDirective> CreateDirectiveAsync(string title)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync(title, cancellationToken: Token));

	private Task<Directive> InitDirectiveAtAsync(string noteRelativePath)
	{
		Vault.WriteVaultFile(noteRelativePath, "# Directive" + Environment.NewLine + "Body." + Environment.NewLine);
		return Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.InitializeFromPathAsync(noteRelativePath, Token));
	}

	private Task<Objective> InitObjectiveAtAsync(string noteRelativePath)
	{
		Vault.WriteVaultFile(noteRelativePath, "# Task" + Environment.NewLine + "Body." + Environment.NewLine);
		return Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.InitializeFromPathAsync(noteRelativePath, Token));
	}

	private Task<EntityExistence> ResolveAsync(string relativePath)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<ISystemApi>()
			.ResolveVaultNoteAsync(relativePath, Token));

	private Task<Objective> UpdateObjectiveAsync(string id, ObjectiveUpdate update)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.UpdateAsync(id, update, Token));

	private Task<StellarDirective> UpdateStellarAsync(string id, StellarDirectiveUpdate update)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.UpdateStellarAsync(id, update, Token));

	private Task<Objective> GetObjectiveAsync(string id)
		=> Vault.QueryAsync(context => context.Incentives.AsNoTracking().OfType<Objective>().SingleAsync(item => item.Id == id, Token));

	private Task<StellarDirective> GetDirectiveAsync(string id)
		=> Vault.QueryAsync(context => context.Directives.AsNoTracking().OfType<StellarDirective>().SingleAsync(item => item.Id == id, Token));

	/// <summary>Every markdown file in the vault (outside metadata) that carries the given PUCK.</summary>
	private string[] NotesAssertingPuck(string puck)
	{
		return Vault.MarkdownFilesUnder(Vault.VaultRoot)
			.Where(path => !path.StartsWith(Vault.Layout.MetadataRoot, StringComparison.OrdinalIgnoreCase))
			.Where(path => File.ReadAllText(path).Contains($"puck: {puck}", StringComparison.Ordinal))
			.ToArray();
	}

	// --- Scenario 3: out-of-place freeform/implicit notes resolve to their origin ---

	[Fact]
	public async Task An_objective_note_under_an_out_of_root_directive_resolves_to_its_entity()
	{
		await InitDirectiveAtAsync("Projects/Alpha/Alpha.md");
		var objective = await InitObjectiveAtAsync("Projects/Alpha/Objectives/Task.md");

		var existence = await ResolveAsync("Projects/Alpha/Objectives/Task.md");

		Assert.True(existence.Exists);
		Assert.Equal(objective.Id, existence.Puck);
		Assert.Equal("Projects/Alpha/Objectives/Task.md", existence.AssociatedNote);
	}

	[Fact]
	public async Task An_objective_note_in_its_directive_folder_outside_the_partition_resolves()
	{
		await CreateDirectiveAsync("Campaign");
		var objective = await InitObjectiveAtAsync("Directives/Campaign/Task.md");

		var existence = await ResolveAsync("Directives/Campaign/Task.md");

		Assert.True(existence.Exists);
		Assert.Equal(objective.Id, existence.Puck);
	}

	[Fact]
	public async Task An_objective_note_in_a_plain_subfolder_of_its_directive_resolves()
	{
		await CreateDirectiveAsync("Campaign");
		var objective = await InitObjectiveAtAsync("Directives/Campaign/Stuff/Task.md");

		var existence = await ResolveAsync("Directives/Campaign/Stuff/Task.md");

		Assert.True(existence.Exists);
		Assert.Equal(objective.Id, existence.Puck);
	}

	[Fact]
	public async Task A_puckless_note_outside_the_root_is_not_an_entity()
	{
		// Stored-only guard: a note with no identity mapping to a row is not an entity, wherever it sits.
		Vault.WriteVaultFile("Projects/Alpha/Freeform Musing.md", "# Musing" + Environment.NewLine + "Just thoughts." + Environment.NewLine);

		var existence = await ResolveAsync("Projects/Alpha/Freeform Musing.md");

		Assert.False(existence.Exists);
	}

	// --- Scenario 4: an API edit rewrites the one real out-of-root file, never a canonical copy ---

	[Fact]
	public async Task Editing_a_mapped_field_on_an_out_of_root_objective_rewrites_it_in_place()
	{
		await InitDirectiveAtAsync("Projects/Alpha/Alpha.md");
		var objective = await InitObjectiveAtAsync("Projects/Alpha/Objectives/Task.md");

		await UpdateObjectiveAsync(objective.Id, new ObjectiveUpdate(College: ObjectiveCollege.Swords));

		var notes = NotesAssertingPuck(objective.Id);
		Assert.Single(notes);
		Assert.StartsWith(Vault.AbsolutePath("Projects/Alpha"), notes[0], StringComparison.OrdinalIgnoreCase);
		Assert.Contains("college: Swords", File.ReadAllText(notes[0]), StringComparison.Ordinal);
	}

	[Fact]
	public async Task Renaming_an_out_of_root_objective_renames_the_real_file_without_duplicating()
	{
		await InitDirectiveAtAsync("Projects/Alpha/Alpha.md");
		var objective = await InitObjectiveAtAsync("Projects/Alpha/Objectives/Task.md");

		await UpdateObjectiveAsync(objective.Id, new ObjectiveUpdate(Title: "Renamed"));

		var notes = NotesAssertingPuck(objective.Id);
		Assert.Single(notes);
		Assert.True(Vault.VaultFileExists("Projects/Alpha/Objectives/Renamed.md"));
		Assert.False(Vault.VaultFileExists("Projects/Alpha/Objectives/Task.md"));
	}

	[Fact] // Refactor BETA part 3: fixed — Freeform.ResolveWriteTargetPath rebases onto the authored container with the new base name, so the self-named folder/file renames in place.
	public async Task Renaming_an_out_of_root_directive_reflects_on_the_real_file_without_duplicating()
	{
		var directive = await InitDirectiveAtAsync("Projects/Campaign/Campaign.md");

		await UpdateStellarAsync(directive.Id, new StellarDirectiveUpdate(Title: "Operations"));

		var notes = NotesAssertingPuck(directive.Id);
		Assert.Single(notes); // never a second copy
		Assert.StartsWith(Vault.AbsolutePath("Projects"), notes[0], StringComparison.OrdinalIgnoreCase); // kept where the user keeps it
		// A directive is Quiet: its title lives only in the filename, so a rename must reach the real file's name — else
		// the next read will resurrect the old title from the stale filename.
		Assert.Contains("Operations", Path.GetFileName(notes[0]), StringComparison.Ordinal);
	}

	// --- Scenario 5: editing an out-of-root note's frontmatter syncs onto the core ---

	[Fact]
	public async Task Editing_the_college_of_an_out_of_root_objective_note_syncs_to_the_core()
	{
		await InitDirectiveAtAsync("Projects/Alpha/Alpha.md");
		var objective = await InitObjectiveAtAsync("Projects/Alpha/Objectives/Task.md");
		var notePath = "Projects/Alpha/Objectives/Task.md";

		SetFrontMatterField(notePath, "college", "Creation");
		await Vault.ReconcileAsync(Vault.AbsolutePath(notePath));

		Assert.Equal(ObjectiveCollege.Creation, (await GetObjectiveAsync(objective.Id)).College);
	}

	[Fact]
	public async Task Editing_the_due_date_of_an_out_of_root_objective_note_syncs_to_the_core()
	{
		await InitDirectiveAtAsync("Projects/Alpha/Alpha.md");
		var objective = await InitObjectiveAtAsync("Projects/Alpha/Objectives/Task.md");
		var notePath = "Projects/Alpha/Objectives/Task.md";

		SetFrontMatterField(notePath, "due", "2026-12-31");
		await Vault.ReconcileAsync(Vault.AbsolutePath(notePath));

		Assert.Equal(new DateOnly(2026, 12, 31), (await GetObjectiveAsync(objective.Id)).Due);
	}

	[Fact]
	public async Task Editing_the_codename_of_an_out_of_root_directive_note_syncs_to_the_core()
	{
		var directive = await InitDirectiveAtAsync("Projects/Campaign/Campaign.md");
		var notePath = "Projects/Campaign/Campaign.md";

		SetFrontMatterField(notePath, "codename", "OPS");
		await Vault.ReconcileAsync(Vault.AbsolutePath(notePath));

		Assert.Equal("OPS", (await GetDirectiveAsync(directive.Id)).Codename);
	}

	/// <summary>
	/// Sets a frontmatter field on the real file: replaces the line if the key is already present (so a mapped enum the
	/// serializer always writes, like <c>college</c>, is not duplicated), otherwise splices it in beside the
	/// always-present <c>puck</c>. The edit lands on whichever file actually holds the note.
	/// </summary>
	private void SetFrontMatterField(string relativePath, string key, string value)
	{
		var absolute = Vault.AbsolutePath(relativePath);
		var lines = File.ReadAllLines(absolute).ToList();
		var existing = lines.FindIndex(line => line.TrimStart().StartsWith(key + ":", StringComparison.Ordinal));
		if (existing >= 0)
		{
			lines[existing] = $"{key}: {value}";
		}
		else
		{
			var puckLine = lines.FindIndex(line => line.TrimStart().StartsWith("puck:", StringComparison.Ordinal));
			Assert.True(puckLine >= 0, "precondition: real frontmatter present");
			lines.Insert(puckLine + 1, $"{key}: {value}");
		}

		File.WriteAllLines(absolute, lines);
	}
}
