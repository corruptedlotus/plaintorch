using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The user is sovereign over where their notes live. Physically moving a freeform/implicit note or a whole directive
/// folder to another valid vault location that does <em>not</em> change its parentage must be absorbed with no
/// aftershock: the entity keeps its identity and its parent, any nested subtree comes along whole, an implicit
/// boundary is not mistaken for a deletion, and no second entity or stray copy appears. Only a move that crosses into
/// or out of a parent's folder is a reparent — and that one case (the deliberate exception) must reflect the new
/// hierarchy, since the path is the authority for it.
///
/// Each move is driven both as a deterministic startup sweep (reboot over the moved state — this MUST be clean) and, for
/// the single-directive case, as the runtime delete-then-create event order the OS reports for a cross-directory move
/// (the order most likely to churn). Where these fail on dev/phase2d they stand as acceptance criteria: a move should
/// never delete-and-recreate, orphan, or duplicate an entity.
/// </summary>
public sealed class PhysicalRelocationNoAftershockTests : VaultTestBase
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private Task<StellarDirective> CreateDirectiveAsync(string title)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync(title, cancellationToken: Token));

	private Task<StellarDirective> CreateChildDirectiveAsync(string parentId, string title)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.CreateFromParentAsync(parentId, title, cancellationToken: Token));

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

	private Task<Objective> CreateObjectiveUnderAsync(string directiveId, string title)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.CreateFromDirectiveAsync(directiveId, title, cancellationToken: Token));

	private string[] NotesAssertingPuck(string puck)
	{
		return Vault.MarkdownFilesUnder(Vault.VaultRoot)
			.Where(path => !path.StartsWith(Vault.Layout.MetadataRoot, StringComparison.OrdinalIgnoreCase))
			.Where(path => File.ReadAllText(path).Contains($"puck: {puck}", StringComparison.Ordinal))
			.ToArray();
	}

	private void MoveDirectory(string fromRelative, string toRelative)
	{
		var to = Vault.AbsolutePath(toRelative);
		Directory.CreateDirectory(Path.GetDirectoryName(to)!);
		Directory.Move(Vault.AbsolutePath(fromRelative), to);
	}

	private void MoveFile(string fromRelative, string toRelative)
	{
		var to = Vault.AbsolutePath(toRelative);
		Directory.CreateDirectory(Path.GetDirectoryName(to)!);
		File.Move(Vault.AbsolutePath(fromRelative), to);
	}

	private async Task RunAsync(string mode, string oldRelative, string newRelative)
	{
		if (mode == "sweep")
		{
			await Vault.SweepAsync();
		}
		else
		{
			// The cross-directory move the OS reports as a delete of the old path then a create of the new one.
			await Vault.ReconcileAsync(Vault.AbsolutePath(oldRelative));
			await Vault.ReconcileAsync(Vault.AbsolutePath(newRelative));
		}
	}

	private Task<int> DirectiveCountAsync()
		=> Vault.QueryAsync(context => context.Directives.CountAsync(Token));

	private Task<StellarDirective?> TryGetDirectiveAsync(string id)
		=> Vault.QueryAsync(context => context.Directives.AsNoTracking().OfType<StellarDirective>()
			.SingleOrDefaultAsync(item => item.Id == id, Token));

	private Task<Objective?> TryGetObjectiveAsync(string id)
		=> Vault.QueryAsync(context => context.Objectives.AsNoTracking()
			.SingleOrDefaultAsync(item => item.Id == id, Token));

	// --- A neutral relocation of a single top-level directive ---

	[Theory]
	[InlineData("sweep")]
	[InlineData("runtime")]
	public async Task Moving_a_top_level_directive_to_a_neutral_location_causes_no_aftershock(string mode)
	{
		var directive = await CreateDirectiveAsync("Campaign");
		var before = await DirectiveCountAsync();
		Assert.True(Vault.VaultFileExists("Directives/Campaign/Campaign.md"));

		MoveDirectory("Directives/Campaign", "Projects/Campaign");
		await RunAsync(mode, "Directives/Campaign/Campaign.md", "Projects/Campaign/Campaign.md");

		var after = await TryGetDirectiveAsync(directive.Id);
		Assert.NotNull(after);                                   // not deleted
		Assert.Null(after!.ParentDirectiveId);                   // still top-level
		Assert.Equal(before, await DirectiveCountAsync());       // no second entity minted
		Assert.Single(NotesAssertingPuck(directive.Id));         // exactly one note
		Assert.True(Vault.VaultFileExists("Projects/Campaign/Campaign.md"));
	}

	// --- A neutral relocation of a whole subtree (children + an objective) ---

	[Fact]
	public async Task Moving_a_directive_with_a_nested_subtree_to_a_neutral_location_causes_no_aftershock()
	{
		var campaign = await CreateDirectiveAsync("Campaign");
		var strike = await CreateChildDirectiveAsync(campaign.Id, "Strike");
		var objective = await CreateObjectiveUnderAsync(campaign.Id, "Take the bridge");
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);
		var before = await DirectiveCountAsync();

		MoveDirectory("Directives/Campaign", "Archive/Campaign");
		await Vault.SweepAsync();

		var movedCampaign = await TryGetDirectiveAsync(campaign.Id);
		var movedStrike = await TryGetDirectiveAsync(strike.Id);
		var movedObjective = await TryGetObjectiveAsync(objective.Id);
		Assert.NotNull(movedCampaign);
		Assert.NotNull(movedStrike);
		Assert.NotNull(movedObjective);
		// Relations are exactly as they were — the move preserved the hierarchy.
		Assert.Null(movedCampaign!.ParentDirectiveId);
		Assert.Equal(campaign.Id, movedStrike!.ParentDirectiveId);
		Assert.Equal(campaign.Id, movedObjective!.DirectiveId);
		Assert.Equal(before, await DirectiveCountAsync());
		// One note each, all under the new home.
		Assert.Single(NotesAssertingPuck(campaign.Id));
		Assert.Single(NotesAssertingPuck(strike.Id));
		Assert.Single(NotesAssertingPuck(objective.Id));
		Assert.True(Vault.VaultFileExists("Archive/Campaign/Strike/Strike.md"));
		Assert.True(Vault.VaultFileExists("Archive/Campaign/Objectives/Take the bridge.md"));
		Assert.False(Directory.Exists(Vault.AbsolutePath("Directives/Campaign")));
	}

	// --- A neutral relocation of an implicit objective note within its parent's folder ---

	[Fact] // Refactor BETA part 3: fixed — the orphan pass skips a begun boundary whose identity is still asserted by a scanned file (it moved, not deleted).
	public async Task Moving_an_implicit_objective_note_within_its_parent_keeps_it_alive()
	{
		await InitDirectiveAtAsync("Projects/Alpha/Alpha.md");
		var objective = await InitObjectiveAtAsync("Projects/Alpha/Objectives/Task.md");

		// Still inside Alpha's folder, just a different subfolder — its parent does not change.
		MoveFile("Projects/Alpha/Objectives/Task.md", "Projects/Alpha/Notebook/Task.md");
		await Vault.SweepAsync();

		var moved = await TryGetObjectiveAsync(objective.Id);
		Assert.NotNull(moved);                                   // the vanished old path must not be read as a deletion
		Assert.Single(NotesAssertingPuck(objective.Id));
		Assert.True(Vault.VaultFileExists("Projects/Alpha/Notebook/Task.md"));
	}

	// --- The deliberate exception: a move that changes the hierarchy IS a reparent ---

	[Fact]
	public async Task Moving_a_child_directive_out_of_its_parent_folder_reparents_it_to_the_top()
	{
		var campaign = await CreateDirectiveAsync("Campaign");
		var strike = await CreateChildDirectiveAsync(campaign.Id, "Strike");
		Assert.True(Vault.VaultFileExists("Directives/Campaign/Strike/Strike.md"));

		// Pulling Strike's folder out to the top level changes its parentage — the path is the authority for it.
		MoveDirectory("Directives/Campaign/Strike", "Directives/Strike");
		await Vault.SweepAsync();

		var reparented = await TryGetDirectiveAsync(strike.Id);
		Assert.NotNull(reparented);
		Assert.Null(reparented!.ParentDirectiveId);              // now top-level, per its new location
		Assert.Single(NotesAssertingPuck(strike.Id));
		Assert.True(Vault.VaultFileExists("Directives/Strike/Strike.md"));
	}
}
