using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Saga;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Lore is a nested Era → Chapter → Act → Phase hierarchy under synced storage. Two properties matter: a synced
/// nested entity must not be hierarchically mutilated because a single file's frontmatter is invalid (validation
/// corrects, it does not cascade), and the hierarchical "beginning" rules that drive active-lore selection must
/// behave coherently with the hierarchy.
/// </summary>
public sealed class LoreHierarchyTests : VaultTestBase
{
	private Task<LorePage> CreateAsync(string? parentPuck, string title, System.DateOnly? beginning = null)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<ILorePageApi>()
			.CreateAsync(new LorePageCreateRequest(parentPuck, title, beginning), TestContext.Current.CancellationToken));

	// Builds the lore index directly against a fixed "now" so active-page selection is deterministic.
	private Task<LoreIndex> LoadIndexAsync(System.DateTime asIn)
		=> Vault.QueryAsync(context => context.LorePages.AsNoTracking()
			.ToLoreIndexAsync(TestContext.Current.CancellationToken, asIn));

	[Fact]
	public async Task Invalid_parent_frontmatter_does_not_mutilate_the_child_hierarchy()
	{
		var era = await CreateAsync(null, "First Age");
		var chapter = await CreateAsync(era.Puck, "The Gathering");

		// Corrupt the parent Era's file with unparseable frontmatter, then let the watcher reconcile it.
		Vault.WriteVaultFile(era.RelativePath, "---" + System.Environment.NewLine + "beginning: [not, a, date" + System.Environment.NewLine + "---" + System.Environment.NewLine + "# broken" + System.Environment.NewLine);
		await Vault.ReconcileAsync(Vault.AbsolutePath(era.RelativePath));

		// The child must still exist, still parented to the era: validation corrects the file, it never orphans or
		// deletes the nested subtree.
		var survived = await Vault.QueryAsync(context => context.LorePages.AsNoTracking()
			.FirstOrDefaultAsync(item => item.Id == chapter.Puck, TestContext.Current.CancellationToken));
		Assert.NotNull(survived);
		Assert.Equal(era.Puck, survived!.ParentId);
		var eraSurvived = await Vault.QueryAsync(context => context.LorePages.AsNoTracking()
			.AnyAsync(item => item.Id == era.Puck, TestContext.Current.CancellationToken));
		Assert.True(eraSurvived);
	}

	[Fact(Skip = "SURFACED DESIGN QUESTION / likely bug in the hierarchical 'beginning' rules (awaiting operator's intended semantics). Reproduced: LoreIndex.MarkActivePages selects the globally latest-begun page and walks up parents UNCONDITIONALLY, so an era whose Beginning is in the future (not yet begun) is reported active because a past-begun child drags it in. Two root causes: nothing constrains a child's Beginning against its parent's (create/update set it freely), and the active walk-up never checks each ancestor has begun. Open calls: constrain child beginnings to the parent's span? stop the walk-up at un-begun ancestors? compute active by descending the hierarchy (latest-begun era, then its latest-begun chapter, …) instead of picking the global-latest leaf?")]
	public async Task Active_lore_does_not_report_an_ancestor_whose_beginning_is_still_in_the_future()
	{
		var today = System.DateOnly.FromDateTime(System.DateTime.Today);

		// No rule constrains a child's beginning against its parent's, so an era can be dated in the future while a
		// chapter under it is dated in the past. The chapter has begun; the era has not.
		var era = await CreateAsync(null, "Future Age", today.AddDays(30));
		await CreateAsync(era.Puck, "Early Chapter", today.AddDays(-10));

		var index = await LoadIndexAsync(System.DateTime.Today);

		// A not-yet-begun era should not be reported as an active narrative position just because a child drags it in.
		Assert.DoesNotContain(index.ActivePages, page => page.Id == era.Puck);
	}
}
