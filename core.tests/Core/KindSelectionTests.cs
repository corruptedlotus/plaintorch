using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// A note's path only proposes what kind of note it is. Where the proposed kind is identity-driven (Freeform, Implicit),
/// the identity the note asserts decides between the identity-driven kinds, so a note is never handed to a kind by its
/// shape and then dropped because its identity is another's. Where the proposed kind is path-bound, the path is the
/// identity and the proposal stands. Note resolution reads a note exactly as the watcher does, so a note resolves to an
/// entity only through the identity its kind's storage declares, and only when an entity of that kind stands behind it.
/// </summary>
public sealed class KindSelectionTests : VaultTestBase
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private Task<Directive> InitDirectiveAtAsync(string note)
	{
		Vault.WriteVaultFile(note, "# Directive" + Environment.NewLine);
		return Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>().InitializeFromPathAsync(note, Token));
	}

	private Task<Objective> InitObjectiveAtAsync(string note)
	{
		Vault.WriteVaultFile(note, "# Task" + Environment.NewLine);
		return Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>().InitializeFromPathAsync(note, Token));
	}

	private Task<EntityExistence> ResolveAsync(string note)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<ISystemApi>().ResolveVaultNoteAsync(note, Token));

	/// <summary>The vault-relative notes (outside the metadata root) asserting a PUCK.</summary>
	private string[] NotesOf(string puck)
	{
		return Vault.MarkdownFilesUnder(Vault.VaultRoot)
			.Where(path => !path.StartsWith(Vault.Layout.MetadataRoot, StringComparison.OrdinalIgnoreCase))
			.Where(path => File.ReadAllText(path).Contains($"puck: {puck}", StringComparison.Ordinal))
			.Select(path => Path.GetRelativePath(Vault.VaultRoot, path).Replace('\\', '/'))
			.ToArray();
	}

	/// <summary>Replaces a frontmatter field on a note, or adds it beside <c>puck</c>.</summary>
	private void SetFrontMatterField(string note, string key, string value)
	{
		var absolute = Vault.AbsolutePath(note);
		var lines = File.ReadAllLines(absolute).ToList();
		var existing = lines.FindIndex(line => line.TrimStart().StartsWith(key + ":", StringComparison.Ordinal));
		if (existing >= 0)
		{
			lines[existing] = $"{key}: {value}";
		}
		else
		{
			var puckLine = lines.FindIndex(line => line.TrimStart().StartsWith("puck:", StringComparison.Ordinal));
			Assert.True(puckLine >= 0, "precondition: the note asserts its identity");
			lines.Insert(puckLine + 1, $"{key}: {value}");
		}

		File.WriteAllLines(absolute, lines);
	}

	[Fact]
	public async Task A_directives_main_note_not_named_like_its_folder_inside_another_directive_syncs_its_edits()
	{
		await InitDirectiveAtAsync("Projects/Campaign/Campaign.md");
		var roadmap = await InitDirectiveAtAsync("Projects/Campaign/Plans/Roadmap.md");

		// By shape, a note inside a directive that is not named like its folder is an objective's; by identity it is this
		// directive's main note.
		SetFrontMatterField("Projects/Campaign/Plans/Roadmap.md", "codename", "RDM");
		var candidate = await Vault.ReconcileAsync(Vault.AbsolutePath("Projects/Campaign/Plans/Roadmap.md"));

		Assert.Equal(nameof(Directive), candidate?.Model.EntityName);
		Assert.Equal("RDM", await Vault.QueryAsync(context => context.Directives.AsNoTracking()
			.Where(item => item.Id == roadmap.Id).Select(item => item.Codename).SingleAsync(Token)));
		Assert.Equal(["Projects/Campaign/Plans/Roadmap.md"], NotesOf(roadmap.Id));
	}

	[Fact]
	public async Task An_objective_note_outside_every_directive_and_its_root_is_tracked_resolved_and_edited_in_place()
	{
		var objective = await InitObjectiveAtAsync("Projects/Task.md");

		// No path shape proposes an objective here, only the catch-all directive model; the identity decides.
		SetFrontMatterField("Projects/Task.md", "college", "Creation");
		var candidate = await Vault.ReconcileAsync(Vault.AbsolutePath("Projects/Task.md"));
		var resolution = await ResolveAsync("Projects/Task.md");
		await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.UpdateAsync(objective.Id, new ObjectiveUpdate(CelestronValue: 3), Token));
		var stored = await Vault.QueryAsync(context => context.Incentives.AsNoTracking().OfType<Objective>()
			.SingleAsync(item => item.Id == objective.Id, Token));

		Assert.Equal(nameof(Objective), candidate?.Model.EntityName);
		Assert.Equal(ObjectiveCollege.Creation, stored.College);
		Assert.Null(stored.DirectiveId);
		Assert.True(resolution.Exists);
		Assert.Equal(objective.Id, resolution.Puck);
		Assert.Equal("Projects/Task.md", resolution.AssociatedNote);
		// An API edit finds the note by the identity it asserts, so it rewrites it where it is.
		Assert.Equal(["Projects/Task.md"], NotesOf(objective.Id));
	}

	[Fact]
	public async Task A_fate_note_moved_into_the_objectives_partition_stays_the_fate()
	{
		var campaign = await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("Campaign", cancellationToken: Token));
		var fate = await Vault.WithScopeAsync(services => services.GetRequiredService<IDeclarativeApi>()
			.CreateFateAsync(new FatePlan("Doom", DirectiveId: campaign.Id), Token));
		fate = await Vault.WithScopeAsync(services => services.GetRequiredService<IDeclarativeApi>()
			.BeginFateBoundaryAsync(fate.Id, Token));
		var from = Assert.Single(NotesOf(fate.Id));
		var to = "Directives/Campaign/Objectives/Doom.md";

		Directory.CreateDirectory(Path.GetDirectoryName(Vault.AbsolutePath(to))!);
		File.Move(Vault.AbsolutePath(from), Vault.AbsolutePath(to));
		SetFrontMatterField(to, "status", nameof(FateStatus.OptOut));
		await Vault.ReconcileEventsWithIssuesAsync([Vault.AbsolutePath(from), Vault.AbsolutePath(to)]);
		var candidate = await Vault.InspectAsync(Vault.AbsolutePath(to));
		var stored = await Vault.QueryAsync(context => context.Incentives.AsNoTracking().OfType<Fate>()
			.SingleOrDefaultAsync(item => item.Id == fate.Id, Token));

		Assert.Equal(nameof(Fate), candidate?.Model.EntityName);
		Assert.NotNull(stored);
		Assert.Equal(FateStatus.OptOut, stored!.Status);
		Assert.Equal(campaign.Id, stored.DirectiveId);
		Assert.Equal([to], NotesOf(fate.Id));
	}

	[Fact]
	public async Task A_note_outside_its_kinds_territory_is_not_adopted_through_its_identity()
	{
		var campaign = await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("Campaign", cancellationToken: Token));
		var objective = await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.CreateFromDirectiveAsync(campaign.Id, "Take the bridge", cancellationToken: Token));
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);
		var from = "Directives/Campaign/Objectives/Take the bridge.md";
		var saga = Path.GetRelativePath(Vault.VaultRoot, Vault.Layout.SagaRoot).Replace('\\', '/');
		var to = $"{saga}/Notes/Take the bridge.md";

		// Saga is a root another kind is declared in: an objective may not be asserted there, so the note is left alone —
		// neither read as the objective nor moved back.
		Directory.CreateDirectory(Path.GetDirectoryName(Vault.AbsolutePath(to))!);
		File.Move(Vault.AbsolutePath(from), Vault.AbsolutePath(to));
		SetFrontMatterField(to, "college", "Creation");
		var moved = Vault.ReadVaultFile(to);
		await Vault.ReconcileEventsWithIssuesAsync([Vault.AbsolutePath(from), Vault.AbsolutePath(to)]);
		var stored = await Vault.QueryAsync(context => context.Incentives.AsNoTracking().OfType<Objective>()
			.SingleAsync(item => item.Id == objective.Id, Token));

		Assert.Equal([to], NotesOf(objective.Id));
		Assert.Equal(moved, Vault.ReadVaultFile(to));
		Assert.NotEqual(ObjectiveCollege.Creation, stored.College);
		Assert.Equal(campaign.Id, stored.DirectiveId);
	}

	[Fact]
	public async Task A_frontmatter_identity_of_a_kind_that_keeps_its_identity_in_the_file_name_resolves_to_nothing()
	{
		var sprint = await Vault.WithScopeAsync(services => services.GetRequiredService<IOnrushSprintApi>()
			.StartNewAsync(cancellationToken: Token));
		Vault.WriteVaultFile("Projects/Sprint notes.md", $"---{Environment.NewLine}puck: {sprint.Id}{Environment.NewLine}---{Environment.NewLine}Notes." + Environment.NewLine);

		var resolution = await ResolveAsync("Projects/Sprint notes.md");

		Assert.False(resolution.Exists);
	}

	[Fact]
	public async Task A_path_that_holds_no_note_resolves_to_nothing_even_where_its_name_is_an_identity()
	{
		var sprint = await Vault.WithScopeAsync(services => services.GetRequiredService<IOnrushSprintApi>()
			.StartNewAsync(cancellationToken: Token));
		var note = Assert.Single(Vault.MarkdownFilesUnder(Vault.Layout.OnrushRoot), path => path.Contains(sprint.Id, StringComparison.OrdinalIgnoreCase));
		var relative = Path.GetRelativePath(Vault.VaultRoot, note).Replace('\\', '/');

		var present = await ResolveAsync(relative);
		File.Delete(note);
		var absent = await ResolveAsync(relative);

		Assert.True(present.Exists);
		Assert.False(absent.Exists);
	}

	[Fact]
	public async Task A_note_in_a_path_bound_kinds_root_is_read_as_that_kind_so_another_kinds_frontmatter_identity_there_resolves_to_nothing()
	{
		var objective = await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.CreateStandaloneAsync("Ship it", cancellationToken: Token));
		var journal = Path.GetRelativePath(Vault.VaultRoot, Vault.Layout.JournalRoot).Replace('\\', '/');
		Vault.WriteVaultFile($"{journal}/Council Meeting - Q3.md", $"---{Environment.NewLine}puck: {objective.Id}{Environment.NewLine}---{Environment.NewLine}Notes." + Environment.NewLine);

		var resolution = await ResolveAsync($"{journal}/Council Meeting - Q3.md");

		Assert.False(resolution.Exists);
	}
}
