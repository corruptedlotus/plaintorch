using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// A declared partition (<c>PartitionUnder</c>) is where a child is placed beneath its parent's folder — when it can be.
/// It cannot when the parent's folder is itself named like the partition, when a folder of that name is an entity's own
/// (its main note asserts an entity's identity, whatever that note or the folder is called), or, before the partition
/// exists, when a file of that name sits in the parent's folder. Children are then placed directly in the parent's folder.
/// And a folder that is an entity's own is never read as a partition, whatever its name, so it contains what sits in it;
/// a note that merely bears the folder's name, asserting nothing, makes it no entity's. Before this, a directive titled like a partition
/// could not be initialised at all, its objectives were invisible to the watcher or handed to the directive above it.
/// </summary>
public sealed class PartitionAvailabilityTests : VaultTestBase
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private Task<T> Directives<T>(Func<IDirectiveApi, Task<T>> call)
		=> Vault.WithScopeAsync(services => call(services.GetRequiredService<IDirectiveApi>()));

	private Task<T> Objectives<T>(Func<IObjectiveApi, Task<T>> call)
		=> Vault.WithScopeAsync(services => call(services.GetRequiredService<IObjectiveApi>()));

	private async Task<Objective> CreateBegunObjectiveAsync(string directiveId, string title)
	{
		var objective = await Objectives(api => api.CreateFromDirectiveAsync(directiveId, title, cancellationToken: Token));
		return await Vault.BeginObjectiveBoundaryAsync(objective.Id);
	}

	private Task<Objective?> StoredObjectiveAsync(string id)
		=> Vault.QueryAsync(context => context.Objectives.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, Token));

	/// <summary>The vault-relative notes (outside the metadata root) asserting a PUCK.</summary>
	private string[] NotesOf(string puck)
	{
		return Vault.MarkdownFilesUnder(Vault.VaultRoot)
			.Where(path => !path.StartsWith(Vault.Layout.MetadataRoot, StringComparison.OrdinalIgnoreCase))
			.Where(path => File.ReadAllText(path).Contains($"puck: {puck}", StringComparison.Ordinal))
			.Select(path => Path.GetRelativePath(Vault.VaultRoot, path).Replace('\\', '/'))
			.ToArray();
	}

	[Theory]
	[InlineData("Projects/Objectives")]
	[InlineData("Directives/Objectives")]
	public async Task A_directive_folder_named_like_a_partition_is_initialised_and_contains_its_objectives(string folder)
	{
		Vault.WriteVaultFile($"{folder}/Objectives.md", "# Objectives" + Environment.NewLine);
		var directive = await Directives(api => api.InitializeFromPathAsync($"{folder}/Objectives.md", Token));

		Vault.WriteVaultFile($"{folder}/Task.md", "# Task" + Environment.NewLine);
		var initialised = await Objectives(api => api.InitializeFromPathAsync($"{folder}/Task.md", Token));
		var created = await CreateBegunObjectiveAsync(directive.Id, "Core made");
		await Vault.SweepAsync();

		Assert.Equal(directive.Id, initialised.DirectiveId);
		Assert.Equal([$"{folder}/Task.md"], NotesOf(initialised.Id));
		// No partition inside a folder of the partition's own name: the child is placed directly in it.
		Assert.Equal([$"{folder}/Core made.md"], NotesOf(created.Id));
		Assert.Equal(directive.Id, (await StoredObjectiveAsync(created.Id))?.DirectiveId);
	}

	[Fact]
	public async Task A_top_level_directive_titled_like_a_partition_keeps_its_objectives_visible_to_the_watcher()
	{
		var directive = await Directives(api => api.CreateStandaloneAsync("Objectives", cancellationToken: Token));
		var objective = await CreateBegunObjectiveAsync(directive.Id, "Top task");

		var candidate = await Vault.InspectAsync(Vault.AbsolutePath("Directives/Objectives/Top task.md"));

		Assert.Equal(["Directives/Objectives/Top task.md"], NotesOf(objective.Id));
		Assert.NotNull(candidate);
		Assert.Equal(nameof(Objective), candidate!.Model.EntityName);
		Assert.Equal(directive.Id, ((Objective)candidate.ParsedModel).DirectiveId);
	}

	[Fact]
	public async Task A_child_directive_titled_like_its_parents_partition_takes_the_place_and_both_keep_their_objectives()
	{
		var campaign = await Directives(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Token));
		var child = await Directives(api => api.CreateFromParentAsync(campaign.Id, "Objectives", cancellationToken: Token));
		var campaignTask = await CreateBegunObjectiveAsync(campaign.Id, "Campaign task");
		var childTask = await CreateBegunObjectiveAsync(child.Id, "Child task");

		await Vault.SweepAsync();

		// The child's own folder occupies the partition's name, so Campaign keeps its new objectives beside it; the child,
		// named like the partition, keeps its own directly in its folder.
		Assert.Equal(["Directives/Campaign/Campaign task.md"], NotesOf(campaignTask.Id));
		Assert.Equal(["Directives/Campaign/Objectives/Child task.md"], NotesOf(childTask.Id));
		Assert.Equal(campaign.Id, (await StoredObjectiveAsync(campaignTask.Id))?.DirectiveId);
		Assert.Equal(child.Id, (await StoredObjectiveAsync(childTask.Id))?.DirectiveId);
	}

	[Fact]
	public async Task A_file_named_like_the_partition_places_new_objectives_directly_in_the_directive_folder()
	{
		var campaign = await Directives(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Token));
		Vault.WriteVaultFile("Directives/Campaign/Objectives.md", "# Objectives" + Environment.NewLine + "The user's own list." + Environment.NewLine);

		var objective = await CreateBegunObjectiveAsync(campaign.Id, "Take the bridge");
		await Vault.SweepAsync();

		Assert.Equal(["Directives/Campaign/Take the bridge.md"], NotesOf(objective.Id));
		Assert.Equal(campaign.Id, (await StoredObjectiveAsync(objective.Id))?.DirectiveId);
		Assert.Contains("The user's own list.", Vault.ReadVaultFile("Directives/Campaign/Objectives.md"), StringComparison.Ordinal);
	}

	[Fact]
	public async Task A_folder_note_that_asserts_no_identity_leaves_the_partition_in_use()
	{
		var campaign = await Directives(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Token));
		var first = await CreateBegunObjectiveAsync(campaign.Id, "Take the bridge");
		// The user's own index note, named like the partition folder it sits in: a name, not an identity.
		Vault.WriteVaultFile("Directives/Campaign/Objectives/Objectives.md", "# Objectives" + Environment.NewLine + "Index." + Environment.NewLine);

		var second = await CreateBegunObjectiveAsync(campaign.Id, "Hold the line");
		await Vault.SweepAsync();

		Assert.Equal(["Directives/Campaign/Objectives/Take the bridge.md"], NotesOf(first.Id));
		Assert.Equal(["Directives/Campaign/Objectives/Hold the line.md"], NotesOf(second.Id));
		Assert.Equal(campaign.Id, (await StoredObjectiveAsync(second.Id))?.DirectiveId);
	}

	[Fact]
	public async Task A_folder_named_like_a_partition_belongs_to_the_directive_whose_main_note_it_holds_whatever_that_note_is_called()
	{
		Vault.WriteVaultFile("Projects/Objectives/Roadmap.md", "# Roadmap" + Environment.NewLine);
		var roadmap = await Directives(api => api.InitializeFromPathAsync("Projects/Objectives/Roadmap.md", Token));
		Vault.WriteVaultFile("Projects/Objectives/Task.md", "# Task" + Environment.NewLine);

		var initialised = await Objectives(api => api.InitializeFromPathAsync("Projects/Objectives/Task.md", Token));
		var created = await CreateBegunObjectiveAsync(roadmap.Id, "Core made");
		await Vault.SweepAsync();

		Assert.Equal(roadmap.Id, initialised.DirectiveId);
		Assert.Equal(["Projects/Objectives/Core made.md"], NotesOf(created.Id));
		Assert.Equal(roadmap.Id, (await StoredObjectiveAsync(created.Id))?.DirectiveId);
	}

	[Fact]
	public async Task An_existing_partition_folder_stays_the_partition()
	{
		var campaign = await Directives(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Token));
		var first = await CreateBegunObjectiveAsync(campaign.Id, "Take the bridge");
		var second = await CreateBegunObjectiveAsync(campaign.Id, "Hold the line");

		Assert.Equal(["Directives/Campaign/Objectives/Take the bridge.md"], NotesOf(first.Id));
		Assert.Equal(["Directives/Campaign/Objectives/Hold the line.md"], NotesOf(second.Id));
	}
}
