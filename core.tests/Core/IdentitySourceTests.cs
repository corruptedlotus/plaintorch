using Microsoft.Extensions.DependencyInjection;
using Pleiades.Diagnostics;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Watcher;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// A note carries an entity's identity only in the one place the entity's storage declares — a quiet kind's frontmatter,
/// an indexed kind's file name (notation-gated) — and every reader takes it from there: discovery, the write path that
/// finds an entity's existing note, and duplicate detection. A user's note merely named with an identity in front of a
/// title is not the entity's note, so a save never rewrites or deletes it and it is never a duplicate.
/// </summary>
public sealed class IdentitySourceTests : VaultTestBase
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private async Task<(StellarDirective Campaign, Objective Objective)> CreateBegunObjectiveAsync()
	{
		var campaign = await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync("Campaign", cancellationToken: Token));
		var objective = await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.CreateFromDirectiveAsync(campaign.Id, "Take the bridge", cancellationToken: Token));
		return (campaign, await Vault.BeginObjectiveBoundaryAsync(objective.Id));
	}

	/// <summary>Writes a user's note named with the identity in front of a title, newer than any other note.</summary>
	private string WriteNamedLikeIdentity(string folder, string id)
	{
		var relative = $"{folder}/{id} - kickoff.md";
		Vault.WriteVaultFile(relative, "Kickoff notes." + Environment.NewLine);
		File.SetLastWriteTimeUtc(Vault.AbsolutePath(relative), DateTime.UtcNow.AddMinutes(5));
		return relative;
	}

	[Fact]
	public async Task A_save_never_takes_a_note_named_with_the_identity_for_a_quiet_entitys_note()
	{
		var (_, objective) = await CreateBegunObjectiveAsync();
		var kickoff = WriteNamedLikeIdentity("Directives/Campaign/Notes", objective.Id);

		await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.UpdateAsync(objective.Id, new ObjectiveUpdate(CelestronValue: 3), Token));

		Assert.True(Vault.VaultFileExists(kickoff));
		Assert.Equal("Kickoff notes." + Environment.NewLine, Vault.ReadVaultFile(kickoff));
		Assert.True(Vault.VaultFileExists("Directives/Campaign/Objectives/Take the bridge.md"));
		Assert.False(Vault.VaultFileExists("Directives/Campaign/Notes/Take the bridge.md"));
	}

	[Fact]
	public async Task A_note_named_with_the_identity_is_no_duplicate_of_a_quiet_entitys_note()
	{
		var (_, objective) = await CreateBegunObjectiveAsync();
		WriteNamedLikeIdentity("Directives/Campaign/Notes", objective.Id);

		await Vault.ReconcileWithIssuesAsync(Vault.AbsolutePath("Directives/Campaign/Objectives/Take the bridge.md"));

		Assert.DoesNotContain(Vault.GetSingleton<OperationStatusRegistry>().GetActiveStatuses(), status =>
			status.ReasonCode == WatcherOperations.DuplicateIdentity);
	}
}
