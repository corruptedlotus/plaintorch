using Microsoft.Extensions.DependencyInjection;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Reparenting through an update. The new parent is taken when the tree stays a tree, and refused when it would not
/// — a parent that does not exist, the directive itself, one of its own descendants, or a directive of the other
/// family (PEP100: a lunar hierarchy stays lunar, a stellar one stellar). An explicit null parent lifts the entity to
/// the top level, while an omitted one leaves it alone. And the note always follows: a composed storage path holds
/// the parent, the path is the authority for it on disk, so any parent-partitioned entity re-homes on a change.
/// </summary>
public sealed class DirectiveReparentingTests : VaultTestBase
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private Task<T> Api<T>(Func<IDirectiveApi, Task<T>> call)
		=> Vault.WithScopeAsync(services => call(services.GetRequiredService<IDirectiveApi>()));

	[Fact]
	public async Task A_stellar_directive_moves_under_another()
	{
		var campaign = await Api(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Token));
		var strike = await Api(api => api.CreateStandaloneAsync("Strike", cancellationToken: Token));

		var moved = await Api(api => api.UpdateStellarAsync(strike.Id, new StellarDirectiveUpdate(ParentDirectiveId: campaign.Id), Token));

		Assert.Equal(campaign.Id, moved.ParentDirectiveId);
		// On disk the path is the authority for a directive's parent, so the note has to follow the move — left
		// behind, the watcher would read the old parent back.
		Assert.True(Vault.VaultFileExists("Directives/Campaign/Strike/Strike.md"));
		Assert.False(Vault.VaultFileExists("Directives/Strike/Strike.md"));
	}

	[Fact]
	public async Task A_moved_directive_takes_what_is_nested_beneath_it()
	{
		var campaign = await Api(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Token));
		var strike = await Api(api => api.CreateStandaloneAsync("Strike", cancellationToken: Token));
		await Api(api => api.CreateFromParentAsync(strike.Id, "Sortie", cancellationToken: Token));

		await Api(api => api.UpdateStellarAsync(strike.Id, new StellarDirectiveUpdate(ParentDirectiveId: campaign.Id), Token));

		Assert.True(Vault.VaultFileExists("Directives/Campaign/Strike/Sortie/Sortie.md"));
		Assert.False(Directory.Exists(Vault.AbsolutePath("Directives/Strike")));
	}

	[Fact]
	public async Task An_ordinary_edit_leaves_the_note_where_it_is()
	{
		var campaign = await Api(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Token));
		var strike = await Api(api => api.CreateFromParentAsync(campaign.Id, "Strike", cancellationToken: Token));

		await Api(api => api.UpdateStellarAsync(strike.Id, new StellarDirectiveUpdate(Codename: "STK"), Token));

		Assert.True(Vault.VaultFileExists("Directives/Campaign/Strike/Strike.md"));
	}

	[Fact]
	public async Task An_objective_with_a_note_follows_its_new_directive()
	{
		var campaign = await Api(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Token));
		var strike = await Api(api => api.CreateStandaloneAsync("Strike", cancellationToken: Token));
		var objective = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.CreateFromDirectiveAsync(strike.Id, "Take the bridge", cancellationToken: Token));
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);
		Assert.Single(NotesOf(objective.Id, "Directives/Strike"));

		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.UpdateAsync(objective.Id, new ObjectiveUpdate(DirectiveId: campaign.Id), Token));

		Assert.Single(NotesOf(objective.Id, "Directives/Campaign"));
		Assert.Empty(NotesOf(objective.Id, "Directives/Strike"));
	}

	[Fact]
	public async Task A_null_parent_lifts_a_directive_to_the_top_level()
	{
		var campaign = await Api(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Token));
		var strike = await Api(api => api.CreateFromParentAsync(campaign.Id, "Strike", cancellationToken: Token));
		await Api(api => api.CreateFromParentAsync(strike.Id, "Sortie", cancellationToken: Token));

		var lifted = await Api(api => api.UpdateStellarAsync(strike.Id, new StellarDirectiveUpdate(ParentDirectiveId: new Optional<string?>(null)), Token));

		Assert.Null(lifted.ParentDirectiveId);
		Assert.True(Vault.VaultFileExists("Directives/Strike/Strike.md"));
		Assert.True(Vault.VaultFileExists("Directives/Strike/Sortie/Sortie.md"));
		Assert.False(Directory.Exists(Vault.AbsolutePath("Directives/Campaign/Strike")));
	}

	[Fact]
	public async Task An_omitted_parent_leaves_the_directive_where_it_is()
	{
		var campaign = await Api(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Token));
		var strike = await Api(api => api.CreateFromParentAsync(campaign.Id, "Strike", cancellationToken: Token));

		var edited = await Api(api => api.UpdateStellarAsync(strike.Id, new StellarDirectiveUpdate(Title: "Strike"), Token));

		Assert.Equal(campaign.Id, edited.ParentDirectiveId);
	}

	[Fact]
	public async Task A_null_directive_makes_an_objective_standalone_and_its_note_follows()
	{
		var campaign = await Api(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Token));
		var objective = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.CreateFromDirectiveAsync(campaign.Id, "Take the bridge", cancellationToken: Token));
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);

		var lifted = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.UpdateAsync(objective.Id, new ObjectiveUpdate(DirectiveId: new Optional<string?>(null)), Token));

		Assert.Null(lifted.DirectiveId);
		Assert.Single(NotesOf(objective.Id, "Objectives"));
		Assert.Empty(NotesOf(objective.Id, "Directives"));
	}

	[Fact]
	public async Task A_decree_with_a_note_follows_its_new_directive()
	{
		var campaign = await Api(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Token));
		var strike = await Api(api => api.CreateStandaloneAsync("Strike", cancellationToken: Token));
		var decree = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDeclarativeApi>()
			.CreateDecreeAsync(new DecreePlan("Stand watch", DirectiveId: strike.Id), Token));
		await Vault.WithScopeAsync(services => services.GetRequiredService<IDeclarativeApi>().BeginDecreeBoundaryAsync(decree.Id, Token));
		Assert.Single(NotesOf(decree.Id, "Directives/Strike"));

		await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDeclarativeApi>()
			.UpdateDecreeAsync(decree.Id, new DecreeUpdate(DirectiveId: campaign.Id), Token));

		Assert.Single(NotesOf(decree.Id, "Directives/Campaign"));
		Assert.Empty(NotesOf(decree.Id, "Directives/Strike"));
	}

	/// <summary>The notes under a vault folder that carry an entity's PUCK — wherever its mode chose to put them.</summary>
	private string[] NotesOf(string id, string vaultFolder)
	{
		var root = Vault.AbsolutePath(vaultFolder);
		return !Directory.Exists(root)
			? []
			: Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
				.Where(path => File.ReadAllText(path).Contains($"puck: {id}", StringComparison.Ordinal))
				.ToArray();
	}

	[Fact]
	public async Task A_directive_cannot_become_its_own_parent()
	{
		var campaign = await Api(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Token));

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			Api(api => api.UpdateStellarAsync(campaign.Id, new StellarDirectiveUpdate(ParentDirectiveId: campaign.Id), Token)));
	}

	[Fact]
	public async Task A_directive_cannot_move_under_its_own_descendant()
	{
		var campaign = await Api(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Token));
		var strike = await Api(api => api.CreateFromParentAsync(campaign.Id, "Strike", cancellationToken: Token));
		var sortie = await Api(api => api.CreateFromParentAsync(strike.Id, "Sortie", cancellationToken: Token));

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			Api(api => api.UpdateStellarAsync(campaign.Id, new StellarDirectiveUpdate(ParentDirectiveId: sortie.Id), Token)));
	}

	[Fact]
	public async Task A_missing_parent_is_refused()
	{
		var campaign = await Api(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Token));

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			Api(api => api.UpdateStellarAsync(campaign.Id, new StellarDirectiveUpdate(ParentDirectiveId: "NOPE"), Token)));
	}

	[Fact]
	public async Task The_two_families_do_not_mix()
	{
		var campaign = await Api(api => api.CreateStandaloneAsync("Campaign", cancellationToken: Token));
		var law = await Api(api => api.CreateLunarAsync("Sleep Law", cancellationToken: Token));

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			Api(api => api.UpdateStellarAsync(campaign.Id, new StellarDirectiveUpdate(ParentDirectiveId: law.Id), Token)));
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			Api(api => api.UpdateLunarAsync(law.Id, new LunarDirectiveUpdate(ParentDirectiveId: campaign.Id), Token)));
	}
}
