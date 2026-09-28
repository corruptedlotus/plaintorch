using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// A folder is an entity's own when a note directly in it asserts that entity's identity — its main note. The note
/// named like its folder is only the default main note: the main note's own name is what names the entity, and the
/// folder may be called anything. So a folder is recognised by what its main note asserts, never by what anything is
/// called, and a folder's owner contains what sits in it while itself being contained by the folder above.
/// </summary>
public sealed class FolderOwnershipTests : VaultTestBase
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private Task<Directive> InitDirectiveAsync(string note)
	{
		Vault.WriteVaultFile(note, "# Directive" + Environment.NewLine);
		return Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>().InitializeFromPathAsync(note, Token));
	}

	private Task<Objective> InitObjectiveAsync(string note)
	{
		Vault.WriteVaultFile(note, "# Task" + Environment.NewLine);
		return Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>().InitializeFromPathAsync(note, Token));
	}

	[Fact]
	public async Task A_directive_owns_the_folder_its_main_note_sits_in_whatever_the_folder_is_called()
	{
		var campaign = await InitDirectiveAsync("Projects/Campaign/Campaign.md");
		var roadmap = await InitDirectiveAsync("Projects/Campaign/Q3 Planning/Roadmap.md");
		var launch = await InitDirectiveAsync("Projects/Campaign/Q3 Planning/Launch/Launch.md");
		var objective = await InitObjectiveAsync("Projects/Campaign/Q3 Planning/Drafts/Task.md");

		Assert.Equal("Roadmap", roadmap.Title);
		Assert.Equal(campaign.Id, roadmap.ParentDirectiveId);
		Assert.Equal(roadmap.Id, launch.ParentDirectiveId);
		Assert.Equal(roadmap.Id, objective.DirectiveId);
	}

	[Fact]
	public async Task The_note_named_like_the_folder_is_its_default_main_note_so_another_directive_beside_it_is_its_child()
	{
		var campaign = await InitDirectiveAsync("Projects/Campaign/Campaign.md");
		var roadmap = await InitDirectiveAsync("Projects/Campaign/Roadmap.md");

		Assert.Null(campaign.ParentDirectiveId);
		Assert.Equal(campaign.Id, roadmap.ParentDirectiveId);
	}
}
