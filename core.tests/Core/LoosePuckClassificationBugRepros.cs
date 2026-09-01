using Microsoft.Extensions.DependencyInjection;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Puck;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Watcher;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Reported dev/phase2a bugs rooted in un-gated loose-PUCK parsing: <c>PuckNamedIdentity.ParseLoose</c> splits any
/// filename on the first <c>" - "</c> into <c>{id} - {title}</c> with no check that the prefix is a valid PUCK for
/// the entity in question. This lets ordinary user notes masquerade as Index-stored entities, which the watcher
/// then destroys and migration resurrects as invalid-PUCK entities. Steering: the loose parse must be notation-gated
/// (tokenized against the model's declared PUCK, as <see cref="VaultFamilyInstantiationResolver"/> already does for
/// concrete-type resolution) — REFACTOR Alpha phase 4 territory.
/// </summary>
public sealed class LoosePuckClassificationBugRepros : VaultTestBase
{
	[Fact(Skip = "CONFIRMED BUG (dev/phase2a; fix in REFACTOR Alpha phase 4). Reproduced: 'Journal/Council Meeting - Q3 Review.md' is classified as a PolarisCycle with the bogus PUCK 'Council Meeting' and SuggestedAction PurgeFile — the watcher destroys the user's non-entity file, and the vault migration resurrects it as an invalid-PUCK entity. Root cause: VaultLoader.ResolveIdentity (Index branch) and discovery trust PuckNamedIdentity.ParseLoose, which splits any ' - ' filename into {id}-{title} with no check that the prefix tokenizes against the model's declared PUCK. Fix: notation-gate the loose parse (PuckTokenizer), as VaultFamilyInstantiationResolver already does.")]
	public async Task Non_entity_dashed_file_is_not_misparsed_into_a_puck_entity()
	{
		// "Council Meeting" is not a valid PolarisCycle {D:p} date; this is a user's ordinary note that merely
		// contains " - ". It must not be classified as an entity carrying the bogus PUCK "Council Meeting".
		Vault.WriteVaultFile("Journal/Council Meeting - Q3 Review.md", "# Notes" + System.Environment.NewLine + "body" + System.Environment.NewLine);

		var candidate = await Vault.WithScopeAsync(services => services
			.GetRequiredService<VaultMarkdownDiscoveryService>()
			.InspectPathAsync(Vault.AbsolutePath("Journal/Council Meeting - Q3 Review.md"), "test", TestContext.Current.CancellationToken));

		Assert.False(
			candidate is not null && string.Equals(candidate.PathId, "Council Meeting", System.StringComparison.Ordinal),
			$"misparsed as {candidate?.Model.EntityName} PUCK '{candidate?.PathId}' with action {candidate?.SuggestedAction}");
	}

	[Fact(Skip = "CONFIRMED BUG (dev/phase2a; fix in REFACTOR Alpha phase 4). Reproduced: a Quiet objective's associated-note resolution returns the unrelated 'Objectives/{id} - Old Draft.md' because PuckEntityResolutionService.PathMatchesIdentity accepts the loose-parsed filename prefix (the ' - ' split) as an identity match. A Quiet entity's note must be matched by frontmatter identity only; the loose-parse branch must be notation-gated (and not applied to Quiet storage).")]
	public async Task Note_resolution_ignores_a_coincidentally_prefixed_unrelated_file()
	{
		var objective = await Vault.WithScopeAsync(services => services
			.GetRequiredService<IObjectiveApi>()
			.CreateStandaloneAsync("Ship it", cancellationToken: TestContext.Current.CancellationToken));

		// A user's unrelated draft whose name coincidentally begins with the objective id + " - ". The objective is
		// Quiet (its real identity is in frontmatter), so this filename must not be accepted as its note.
		Vault.WriteVaultFile($"Objectives/{objective.Id} - Old Draft.md", "# draft" + System.Environment.NewLine);

		var existence = await Vault.WithScopeAsync(services => services
			.GetRequiredService<PuckEntityResolutionService>()
			.ResolveAsync(objective.Id, TestContext.Current.CancellationToken));

		Assert.NotEqual($"Objectives/{objective.Id} - Old Draft.md", existence.AssociatedNote);
	}
}
