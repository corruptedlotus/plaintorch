using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Note resolution is stored-only and identity-only: a note resolves to an entity only through the identity it carries —
/// the one its kind's storage puts in the path (an Index filename token, a lore hierarchy) or in its frontmatter — and
/// only when that identity maps to a row in the database. A plain note that merely sits in an entity location, or merely
/// shares an entity's title, is not that entity: a title is not an identity.
/// </summary>
public sealed class NoteResolutionTests : VaultTestBase
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private Task<Pleiades.Plaintorch.Api.Contracts.EntityExistence> ResolveAsync(string notePath)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<ISystemApi>().ResolveVaultNoteAsync(notePath, Token));

	private async Task<Objective> CreateBegunObjectiveAsync(string directiveId, string title)
	{
		var objective = await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.CreateFromDirectiveAsync(directiveId, title, cancellationToken: Token));
		return await Vault.BeginObjectiveBoundaryAsync(objective.Id);
	}

	[Theory]
	[InlineData("Directives/Campaign/Notes/Take the bridge.md")]
	[InlineData("Objectives/Take the bridge.md")]
	public async Task A_plain_note_sharing_an_objectives_title_is_not_that_objective(string notePath)
	{
		var campaign = await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("Campaign", cancellationToken: Token));
		var objective = await CreateBegunObjectiveAsync(campaign.Id, "Take the bridge");
		Vault.WriteVaultFile(notePath, "Just the user's notes about it." + Environment.NewLine);

		var plain = await ResolveAsync(notePath);
		var real = await ResolveAsync("Directives/Campaign/Objectives/Take the bridge.md");

		Assert.False(plain.Exists);
		Assert.True(real.Exists);
		Assert.Equal(objective.Id, real.Puck);
	}

	[Fact]
	public async Task A_dashed_title_objective_resolves_by_its_identity()
	{
		var campaign = await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("2024 - Roadmap", cancellationToken: Token));
		var objective = await CreateBegunObjectiveAsync(campaign.Id, "Q1 - Ship it");

		var resolution = await ResolveAsync("Directives/2024 - Roadmap/Objectives/Q1 - Ship it.md");

		Assert.True(resolution.Exists);
		Assert.Equal(objective.Id, resolution.Puck);
		Assert.Equal(nameof(Objective), resolution.EntityType);
	}

	[Fact]
	public async Task Plain_note_in_an_entity_location_resolves_as_not_an_entity()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		// Any '.md' under the Journal root matches the Polaris cycle model's path shape, but a plain note is not a
		// cycle: it carries no cycle identity and maps to no stored entity.
		var journalRelative = Path.GetRelativePath(Vault.VaultRoot, Vault.Layout.JournalRoot).Replace('\\', '/');
		var notePath = $"{journalRelative}/Stray Thoughts.md";
		Vault.WriteVaultFile(notePath, "Just some notes, not a cycle." + Environment.NewLine);

		var resolution = await Vault.WithScopeAsync(services => services
			.GetRequiredService<ISystemApi>()
			.ResolveVaultNoteAsync(notePath, cancellationToken));

		Assert.False(resolution.Exists);
		Assert.Null(resolution.Entity);
	}
}
