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

	[Fact] // Phase 4 (D17): implemented — LorePageApiService constrains a child's Beginning to its parent's span.
	public async Task A_child_beginning_before_its_parent_is_rejected()
	{
		var today = System.DateOnly.FromDateTime(System.DateTime.Today);
		var era = await CreateAsync(null, "Future Age", today.AddDays(30));

		// A chapter cannot begin before its era: the hierarchy is a nested timeline, so a child's beginning is
		// constrained to its parent's span.
		await Assert.ThrowsAsync<System.InvalidOperationException>(() =>
			CreateAsync(era.Puck, "Early Chapter", today.AddDays(-10)));
	}
}
