using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Saga;
using Pleiades.Tests.Harness;
using Pleiades.Vault;
using Pleiades.Vault.Watcher;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The path-sync model list is a projection of the entity model catalog (REFACTOR Alpha phase 3). This pins the
/// projected set byte-for-byte so a change to what the watcher discovers is a deliberate edit, and asserts the
/// catalog coverage/coherence the bootstrapper enforces at activation.
/// </summary>
public sealed class PathSyncModelCatalogTests : VaultTestBase
{
	private static readonly (Type Type, VaultStorageMode Mode, VaultStorageShape Shape)[] Expected =
	[
		(typeof(Directive), VaultStorageMode.Freeform, VaultStorageShape.SelfNamedDirectory),
		(typeof(Objective), VaultStorageMode.Implicit, VaultStorageShape.SingleFile),
		(typeof(Fate), VaultStorageMode.Implicit, VaultStorageShape.SingleFile),
		(typeof(Decree), VaultStorageMode.Implicit, VaultStorageShape.SingleFile),
		(typeof(OnrushSprint), VaultStorageMode.Enforced, VaultStorageShape.SelfNamedDirectory),
		(typeof(ExecutiveOrder), VaultStorageMode.Synced, VaultStorageShape.SingleFile),
		(typeof(PolarisCycle), VaultStorageMode.Enforced, VaultStorageShape.SingleFile),
		(typeof(LorePage), VaultStorageMode.FileFirst, VaultStorageShape.SelfNamedDirectory),
	];

	[Fact]
	public async Task Projected_models_match_the_expected_type_mode_and_shape_set()
	{
		await Vault.WithScopeAsync(services =>
		{
			var models = services.GetRequiredService<VaultPathSyncModelCatalog>().GetModels();

			Assert.Equal(Expected.Length, models.Count);
			foreach (var (type, mode, shape) in Expected)
			{
				var model = Assert.Single(models, candidate => candidate.EntityType == type);
				Assert.Equal(mode, model.Mode);
				Assert.Equal(shape, model.Shape);
			}

			// The directive model anchors the abstract family; its declared fallback member stays stellar (identity
			// selects lunar when present — see FamilyInstantiationResolverTests).
			var directive = models.Single(model => model.EntityType == typeof(Directive));
			Assert.Equal(typeof(StellarDirective), directive.InstantiationType);

			return Task.CompletedTask;
		});
	}

	[Fact]
	public async Task Validation_accepts_the_declared_catalog()
	{
		// The bootstrapper already runs this at activation; assert it directly so the projection's coverage and
		// shape coherence are pinned as an explicit contract, not only an implicit setup side effect.
		await Vault.WithScopeAsync(services =>
		{
			var pathSync = services.GetRequiredService<VaultPathSyncModelCatalog>();
			var catalog = services.GetRequiredService<VaultEntityModelCatalog>();

			pathSync.ValidateAgainstCatalog(catalog);

			return Task.CompletedTask;
		});
	}
}
