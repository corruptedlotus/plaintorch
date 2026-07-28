using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Canonical rewrites must normalize frontmatter and identity while preserving the markdown body verbatim.
/// </summary>
public sealed class BodyPreservationTests : VaultTestBase
{
	[Fact]
	public async Task Api_update_rewrites_frontmatter_but_preserves_body()
	{
		var objective = await Vault.SeedStandaloneObjectiveAsync("Ship it");
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);

		var path = Vault.AbsolutePath("Objectives/Ship it.md");
		var withBody = File.ReadAllText(path).TrimEnd() + Environment.NewLine + "My precious body." + Environment.NewLine;
		File.WriteAllText(path, withBody);

		await Vault.WithScopeAsync(services =>
			services.GetRequiredService<IObjectiveApi>().UpdateAsync(objective.Id, new ObjectiveUpdate(College: ObjectiveCollege.Lore)));

		var after = File.ReadAllText(path);
		Assert.Contains("My precious body.", after);
		Assert.Contains("college: Lore", after);
	}
}
