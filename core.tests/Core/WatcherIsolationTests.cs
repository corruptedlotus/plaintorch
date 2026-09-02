using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Puck;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Watcher isolation: a file that is outside every entity root, carries no PUCK identity, or is merely an extra file
/// living beside a self-named entity's primary must never be reconciled into an entity, never be modified or deleted
/// by the watcher, and never be adopted as an entity's associated note without an explicit boundary/initialization.
/// These are the guarantees a user's own notes rely on to coexist with managed vault content. Cases that currently
/// break these guarantees are pinned separately as skipped repros (see LoosePuckClassificationBugRepros).
/// </summary>
public sealed class WatcherIsolationTests : VaultTestBase
{
	private Task<int> IncentiveCountAsync()
		=> Vault.QueryAsync(context => context.Incentives.CountAsync(TestContext.Current.CancellationToken));

	private Task<int> DirectiveCountAsync()
		=> Vault.QueryAsync(context => context.Directives.CountAsync(TestContext.Current.CancellationToken));

	[Fact]
	public async Task A_plain_file_outside_every_entity_root_is_never_a_candidate_or_touched()
	{
		Vault.WriteVaultFile("Inbox/Loose Thoughts.md", "# thoughts" + System.Environment.NewLine + "body" + System.Environment.NewLine);

		Assert.Null(await Vault.InspectAsync(Vault.AbsolutePath("Inbox/Loose Thoughts.md")));

		await Vault.SweepAsync();

		Assert.True(Vault.VaultFileExists("Inbox/Loose Thoughts.md"));
		Assert.Equal(0, await IncentiveCountAsync());
		Assert.Equal(0, await DirectiveCountAsync());
	}

	[Fact]
	public async Task A_dashed_file_outside_every_root_is_not_misparsed_or_touched()
	{
		// The Bug-B loose-PUCK misparse is root-scoped: outside any scanned root, a " - " filename is never even a
		// candidate, so it cannot be turned into an invalid-PUCK entity or purged.
		Vault.WriteVaultFile("Archive/Board Meeting - 2026 Q3.md", "notes" + System.Environment.NewLine);

		Assert.Null(await Vault.InspectAsync(Vault.AbsolutePath("Archive/Board Meeting - 2026 Q3.md")));

		await Vault.SweepAsync();

		Assert.True(Vault.VaultFileExists("Archive/Board Meeting - 2026 Q3.md"));
		Assert.Equal(0, await IncentiveCountAsync());
	}

	[Fact]
	public async Task An_extra_file_beside_a_self_named_directive_is_not_made_an_entity_or_associated()
	{
		var directive = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("Campaign", cancellationToken: TestContext.Current.CancellationToken));

		// A user's aside living inside the directive's own folder, beside Campaign/Campaign.md.
		Vault.WriteVaultFile("Directives/Campaign/Side Note.md", "# aside" + System.Environment.NewLine);

		var incentivesBefore = await IncentiveCountAsync();
		await Vault.SweepAsync();

		Assert.Equal(incentivesBefore, await IncentiveCountAsync());
		Assert.True(Vault.VaultFileExists("Directives/Campaign/Side Note.md"));

		var existence = await Vault.WithScopeAsync(services => services
			.GetRequiredService<PuckEntityResolutionService>()
			.ResolveAsync(directive.Id, TestContext.Current.CancellationToken));
		Assert.Equal("Directives/Campaign/Campaign.md", existence.AssociatedNote);
	}

	[Fact]
	public async Task An_unbegun_implicit_entity_is_never_reflected_onto_a_nonexistent_note()
	{
		// Implicit entities exist database-first with no file until a boundary is explicitly begun. The watcher must
		// never materialize or reflect onto a note that does not exist (philosophy: no reflection for a noteless entity).
		var objective = await Vault.SeedStandaloneObjectiveAsync("Quiet Goal");

		await Vault.SweepAsync();

		Assert.False(Vault.VaultFileExists("Objectives/Quiet Goal.md"));
		var stillPresent = await Vault.QueryAsync(context => context.Incentives
			.AnyAsync(item => item.Id == objective.Id, TestContext.Current.CancellationToken));
		Assert.True(stillPresent);
	}

	[Fact]
	public async Task A_file_with_no_puck_identity_never_creates_an_entity_from_a_freeform_root()
	{
		// A self-named directory with a title-only file and no frontmatter PUCK is not a freeform entity: freeform
		// belonging is identity-driven, so with no identity there is nothing to adopt.
		Vault.WriteVaultFile("Musings/Musings.md", "# musings" + System.Environment.NewLine + "just thinking" + System.Environment.NewLine);

		await Vault.SweepAsync();

		Assert.True(Vault.VaultFileExists("Musings/Musings.md"));
		Assert.Equal(0, await DirectiveCountAsync());
		Assert.Equal(0, await IncentiveCountAsync());
	}
}
