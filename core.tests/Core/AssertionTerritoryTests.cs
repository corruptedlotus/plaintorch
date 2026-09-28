using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Resources;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// An identity-driven kind (Freeform, Implicit) may be asserted anywhere in the vault, within limits read from its own
/// declarations rather than from any particular kind: a self-named kind's note owns the folder it sits in, so that folder
/// may not be the vault root, an entity root or a partition; and no note may sit under a root declared by a kind outside
/// its own family and the families of the kinds that may contain it. Initialising a note inside those limits is passive:
/// the note is read where it is, its parent is taken from where it sits, and nothing is moved or recreated.
/// </summary>
public sealed class AssertionTerritoryTests : VaultTestBase
{
	private const string BodyMarker = "The user's own words.";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private Task<Directive> InitDirectiveAsync(string note)
	{
		Vault.WriteVaultFile(note, "# Directive" + Environment.NewLine + BodyMarker + Environment.NewLine);
		return Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>().InitializeFromPathAsync(note, Token));
	}

	private Task<Objective> InitObjectiveAsync(string note)
	{
		Vault.WriteVaultFile(note, "# Task" + Environment.NewLine + BodyMarker + Environment.NewLine);
		return Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>().InitializeFromPathAsync(note, Token));
	}

	private string[] MarkdownNotes()
	{
		return Vault.MarkdownFilesUnder(Vault.VaultRoot)
			.Where(path => !path.StartsWith(Vault.Layout.MetadataRoot, StringComparison.OrdinalIgnoreCase))
			.Select(path => Path.GetRelativePath(Vault.VaultRoot, path).Replace('\\', '/'))
			.Order(StringComparer.Ordinal)
			.ToArray();
	}

	[Theory]
	[InlineData("Saga/Notes/Task.md")]
	[InlineData("Onrush/Notes/Task.md")]
	public async Task An_objective_is_not_initialised_under_a_root_another_kind_is_declared_in(string note)
	{
		var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => InitObjectiveAsync(note));

		Assert.Contains(WatcherMessages.PathViolations.UnderForeignRoot, refusal.Message, StringComparison.Ordinal);
		Assert.DoesNotContain("puck:", Vault.ReadVaultFile(note), StringComparison.Ordinal);
	}

	[Fact]
	public async Task A_directive_is_not_initialised_under_a_root_another_kind_is_declared_in()
	{
		var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => InitDirectiveAsync("Objectives/Campaign/Campaign.md"));

		Assert.Contains(WatcherMessages.PathViolations.UnderForeignRoot, refusal.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task A_directive_note_cannot_own_a_partition()
	{
		await InitDirectiveAsync("Directives/Campaign/Campaign.md");
		Directory.CreateDirectory(Vault.AbsolutePath("Directives/Campaign/Objectives"));

		var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => InitDirectiveAsync("Directives/Campaign/Objectives/Plan.md"));

		Assert.Contains(WatcherMessages.PathViolations.CannotOwnPartition, refusal.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task A_single_file_kind_may_sit_directly_in_its_own_root()
	{
		var objective = await InitObjectiveAsync("Objectives/Task.md");

		Assert.Null(objective.DirectiveId);
		Assert.Equal(["Objectives/Task.md"], MarkdownNotes());
	}

	[Fact]
	public async Task Initialising_notes_inside_the_territory_moves_nothing_and_reads_the_hierarchy_from_where_they_sit()
	{
		var campaign = await InitDirectiveAsync("Projects/Campaign/Campaign.md");
		var roadmap = await InitDirectiveAsync("Projects/Campaign/Plans/Roadmap.md");
		var objective = await InitObjectiveAsync("Projects/Campaign/Plans/Drafts/Task.md");

		Assert.Null(campaign.ParentDirectiveId);
		Assert.Equal(campaign.Id, roadmap.ParentDirectiveId);
		Assert.Equal(roadmap.Id, objective.DirectiveId);
		Assert.Equal(
			["Projects/Campaign/Campaign.md", "Projects/Campaign/Plans/Drafts/Task.md", "Projects/Campaign/Plans/Roadmap.md"],
			MarkdownNotes());
		foreach (var note in MarkdownNotes())
		{
			Assert.Contains(BodyMarker, Vault.ReadVaultFile(note), StringComparison.Ordinal);
		}
	}
}
