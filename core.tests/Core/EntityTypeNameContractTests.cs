using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Puck;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Pins the runtime type names the API serializes as <c>@type</c>.
/// </summary>
/// <remarks>
/// The frontend routes an entity to its repository by this name (see
/// <c>sdk.ts/plaintorch/repository/repositories.ts</c>). Renaming a concrete entity therefore breaks that
/// routing silently — a surface asking for the old name resolves nothing and simply renders blank, which is
/// exactly how <c>Directive</c> becoming <c>StellarDirective</c> went unnoticed.
///
/// This test exists so that rename fails here, next to the change, instead of downstream in a UI nobody is
/// looking at. If it fails, update the list *and* the frontend's routing map together.
/// </remarks>
public sealed class EntityTypeNameContractTests : VaultTestBase
{
	/// <summary>
	/// Concrete PUCK-named entity types, which are what the frontend tracks by identity.
	/// Mirrored by the routing map in <c>PlaintorchRepositories</c>.
	/// </summary>
	private static readonly string[] ExpectedPuckNamedEntityTypeNames =
	[
		"Checkpoint",
		"Decree",
		"ExecutiveOrder",
		"Fate",
		"LorePage",
		"LunarDirective",
		"Objective",
		"OnrushSprint",
		"PolarisCycle",
		"StellarDirective",
	];

	[Fact]
	public async Task Puck_named_entity_type_names_match_the_frontend_routing_contract()
	{
		var actual = await Vault.WithScopeAsync(services =>
		{
			var context = services.GetRequiredService<PlainfraContext>();
			var names = context.Model.GetEntityTypes()
				.Select(entityType => entityType.ClrType)
				.Where(clrType => typeof(IPuckNamedEntity).IsAssignableFrom(clrType) && !clrType.IsAbstract)
				.Select(clrType => clrType.Name)
				.Distinct()
				.OrderBy(name => name, StringComparer.Ordinal)
				.ToArray();
			return Task.FromResult(names);
		});

		Assert.Equal(ExpectedPuckNamedEntityTypeNames, actual);
	}

	[Fact]
	public async Task Every_puck_named_entity_carries_both_halves_of_the_identity_the_frontend_recognizes()
	{
		// The frontend recognizes an entity structurally — a string `id` and a `title` — rather than by
		// name, so that a rename cannot make one invisible. That only holds while the contract does.
		var offenders = await Vault.WithScopeAsync(services =>
		{
			var context = services.GetRequiredService<PlainfraContext>();
			var bad = context.Model.GetEntityTypes()
				.Select(entityType => entityType.ClrType)
				.Where(clrType => typeof(IPuckNamedEntity).IsAssignableFrom(clrType) && !clrType.IsAbstract)
				.Where(clrType =>
					clrType.GetProperty(nameof(IPuckNamedEntity.Id))?.PropertyType != typeof(string)
					|| clrType.GetProperty(nameof(IPuckNamedEntity.Title))?.PropertyType != typeof(string))
				.Select(clrType => clrType.Name)
				.ToArray();
			return Task.FromResult(bad);
		});

		Assert.Empty(offenders);
	}
}
