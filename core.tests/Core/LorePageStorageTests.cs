using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Lore pages are a strict Era → Chapter → Act → Phase hierarchy of one entity, created database-first under synced
/// storage: nested self-named folders, per-level index numbering with the documented reset rules, and index edits
/// that re-key the page and its whole subtree.
/// </summary>
public sealed class LorePageStorageTests : VaultTestBase
{
	private Task<LorePageRecord> CreateAsync(string? parentPuck, string title)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<ILorePageApi>()
			.CreateAsync(new LorePageCreateRequest(parentPuck, title), TestContext.Current.CancellationToken));

	[Fact]
	public async Task Create_composes_nested_hierarchy_on_disk()
	{
		var era = await CreateAsync(null, "First Age");
		var chapter = await CreateAsync(era.Puck, "The Gathering");
		var act = await CreateAsync(chapter.Puck, "Dawn");
		var phase = await CreateAsync(act.Puck, "Waking");

		Assert.Equal("Era1", era.Puck);
		Assert.Equal("Era1/Cha1", chapter.Puck);
		Assert.Equal("Era1/Cha1/Act1", act.Puck);
		Assert.Equal("Era1/Cha1/Act1/p1", phase.Puck);

		Assert.Equal("Era1", chapter.ParentPuck);
		Assert.Equal("Era1/Cha1/Act1", phase.ParentPuck);
		Assert.Equal("p", phase.Level);

		// Each level's file sits nested inside its parent's folder, and the phase file exists at the deepest path.
		Assert.True(Vault.VaultFileExists(phase.RelativePath));
		Assert.StartsWith(Path.GetDirectoryName(era.RelativePath)!, Path.GetDirectoryName(chapter.RelativePath)!, StringComparison.OrdinalIgnoreCase);
		Assert.StartsWith(Path.GetDirectoryName(act.RelativePath)!, Path.GetDirectoryName(phase.RelativePath)!, StringComparison.OrdinalIgnoreCase);
		Assert.Contains($"puck: {phase.Puck}", Vault.ReadVaultFile(phase.RelativePath));
	}

	[Fact]
	public async Task Index_numbering_follows_the_reset_rules()
	{
		var eraA = await CreateAsync(null, "Era A");
		var eraB = await CreateAsync(null, "Era B");
		Assert.Equal(1, eraA.Era);
		Assert.Equal(2, eraB.Era);

		// Chapter numbering never resets: a chapter in the second era keeps climbing globally.
		var chapterA1 = await CreateAsync(eraA.Puck, "A-First");
		var chapterB = await CreateAsync(eraB.Puck, "B-First");
		Assert.Equal(1, chapterA1.Chapter);
		Assert.Equal(2, chapterB.Chapter);

		// Act numbering resets each Era, so it spans chapters within an era rather than resetting per chapter.
		var actUnderA1 = await CreateAsync(chapterA1.Puck, "A1-Act");
		var chapterA3 = await CreateAsync(eraA.Puck, "A-Second");
		var actUnderA3 = await CreateAsync(chapterA3.Puck, "A3-Act");
		Assert.Equal(3, chapterA3.Chapter);
		Assert.Equal(1, actUnderA1.Act);
		Assert.Equal(2, actUnderA3.Act);

		// Phase numbering resets each Act.
		var phase1 = await CreateAsync(actUnderA1.Puck, "P1");
		var phase2 = await CreateAsync(actUnderA1.Puck, "P2");
		var phaseOtherAct = await CreateAsync(actUnderA3.Puck, "P-other");
		Assert.Equal(1, phase1.Phase);
		Assert.Equal(2, phase2.Phase);
		Assert.Equal(1, phaseOtherAct.Phase);
	}

	[Fact]
	public async Task Set_index_rekeys_the_page_and_cascades_to_descendants()
	{
		var era = await CreateAsync(null, "First Age");
		var chapter = await CreateAsync(era.Puck, "The Gathering");
		var oldEraDirectory = Path.GetDirectoryName(Vault.AbsolutePath(era.RelativePath))!;

		var renumbered = await Vault.WithScopeAsync(services => services.GetRequiredService<ILorePageApi>()
			.SetIndexAsync("Era1", 5, TestContext.Current.CancellationToken));

		Assert.NotNull(renumbered);
		Assert.Equal("Era5", renumbered!.Puck);
		Assert.Equal(5, renumbered.Era);

		// The descendant chapter is re-keyed under the new era id, index and parent alike.
		var child = await Vault.QueryAsync(context => context.LorePages.AsNoTracking()
			.FirstOrDefaultAsync(item => item.Title == "The Gathering", TestContext.Current.CancellationToken));
		Assert.NotNull(child);
		Assert.Equal("Era5/Cha1", child!.Id);
		Assert.Equal("Era5", child.ParentId);
		Assert.Equal(5, child.Era);
		Assert.Equal(1, child.Chapter);

		// The subtree moved on disk: the new folder holds both files, the old era folder is gone, and the child's
		// frontmatter PUCK reflects the new id.
		Assert.True(Vault.VaultFileExists(renumbered.RelativePath));
		Assert.True(Vault.VaultFileExists(child.RelativePath));
		Assert.False(Directory.Exists(oldEraDirectory));
		Assert.Contains("puck: Era5/Cha1", Vault.ReadVaultFile(child.RelativePath));

		// The old id no longer resolves.
		var stale = await Vault.QueryAsync(context => context.LorePages.AsNoTracking()
			.AnyAsync(item => item.Id == "Era1" || item.Id == "Era1/Cha1", TestContext.Current.CancellationToken));
		Assert.False(stale);
	}

	[Fact]
	public async Task Set_index_rejects_an_index_already_taken_by_a_sibling()
	{
		await CreateAsync(null, "Era A");
		await CreateAsync(null, "Era B");

		await Assert.ThrowsAsync<InvalidOperationException>(() => Vault.WithScopeAsync(services =>
			services.GetRequiredService<ILorePageApi>().SetIndexAsync("Era1", 2, TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task Delete_is_refused_while_children_remain()
	{
		var era = await CreateAsync(null, "First Age");
		await CreateAsync(era.Puck, "The Gathering");

		await Assert.ThrowsAsync<InvalidOperationException>(() => Vault.WithScopeAsync(services =>
			services.GetRequiredService<ILorePageApi>().DeleteAsync("Era1", TestContext.Current.CancellationToken)));
	}
}
