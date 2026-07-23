using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Saga;
using Pleiades.Tests.Harness;
using Pleiades.Vault;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The entity model catalog reflects identity/storage declarations into one coherent, validated registry, and
/// the entity gateway serves declaration-driven data access without per-type dispatch.
/// </summary>
public sealed class EntityModelCatalogTests : VaultTestBase
{
	[Fact]
	public void Catalog_enumerates_every_declared_entity_with_unique_kinds()
	{
		var catalog = Vault.GetSingleton<VaultEntityModelCatalog>();
		var models = catalog.GetModels();

		Type[] expected =
		[
			typeof(Directive), typeof(StellarDirective), typeof(LunarDirective),
			typeof(Objective), typeof(Fate), typeof(Decree),
			typeof(OnrushSprint), typeof(ExecutiveOrder), typeof(PolarisCycle), typeof(LorePage),
		];
		foreach (var type in expected)
		{
			Assert.Contains(models, model => model.EntityType == type);
		}

		var kinds = models.Where(model => model.Kind is not null).Select(model => model.Kind!).ToArray();
		Assert.Equal(kinds.Length, kinds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
	}

	[Fact]
	public void Storage_policies_anchor_per_declaration_not_per_root()
	{
		var catalog = Vault.GetSingleton<VaultEntityModelCatalog>();

		// Stellar inherits the family's storage policy from the abstract anchor.
		var stellar = catalog.GetRequired(typeof(StellarDirective));
		Assert.Equal(typeof(Directive), stellar.StorageDeclaringType);
		Assert.Equal(VaultLocationKeys.Directives, stellar.Storage!.LocationKey);
		Assert.Equal(VaultStorageMode.Freeform, stellar.Storage.Mode);

		// Lunar overrides with its own dedicated policy.
		var lunar = catalog.GetRequired(typeof(LunarDirective));
		Assert.Equal(typeof(LunarDirective), lunar.StorageDeclaringType);
		Assert.Equal(VaultLocationKeys.Moonlight, lunar.Storage!.LocationKey);
		Assert.Equal(VaultStorageMode.Freeform, lunar.Storage.Mode);

		// The abstract anchor is flagged as such and carries the family declaration.
		var anchor = catalog.GetRequired(typeof(Directive));
		Assert.True(anchor.IsAbstract);
		Assert.Equal("directive", anchor.Kind);
	}

	[Fact]
	public async Task Validation_accepts_the_current_application_model()
	{
		var catalog = Vault.GetSingleton<VaultEntityModelCatalog>();
		await Vault.QueryAsync<object?>(context =>
		{
			catalog.Validate(context.Model);
			return Task.FromResult<object?>(null);
		});
	}

	[Fact]
	public async Task Gateway_clone_snapshots_every_mapped_scalar()
	{
		var stellar = new StellarDirective
		{
			Id = "A12345678",
			Title = "Campaign",
			Codename = "AURORA",
			Status = DirectiveStatus.Active,
			Tags = ["ops", "q3"],
			Due = new DateOnly(2026, 8, 1),
			StartDate = new DateOnly(2026, 7, 1),
			EndDate = new DateOnly(2026, 9, 1),
		};

		var clone = await Vault.WithScopeAsync(services => Task.FromResult(
			(StellarDirective)services.GetRequiredService<VaultEntityGateway>().CloneScalars(stellar)));

		Assert.NotSame(stellar, clone);
		Assert.Equal(stellar.Id, clone.Id);
		Assert.Equal(stellar.Title, clone.Title);
		Assert.Equal(stellar.Codename, clone.Codename);
		Assert.Equal(stellar.Status, clone.Status);
		Assert.Equal(stellar.Tags, clone.Tags);
		Assert.Equal(stellar.Due, clone.Due);
		Assert.Equal(stellar.StartDate, clone.StartDate);
		Assert.Equal(stellar.EndDate, clone.EndDate);

		// Snapshots stay stable when the source is updated by assignment.
		stellar.Title = "Renamed";
		stellar.Tags = ["renamed"];
		Assert.Equal("Campaign", clone.Title);
		Assert.Equal(["ops", "q3"], clone.Tags);
	}

	[Fact]
	public async Task Gateway_clone_recurses_into_owned_references()
	{
		var cycle = new PolarisCycle
		{
			Id = "20260101",
			Title = "New Year",
			Forecast = new PolarisForecast
			{
				ForecastReference = new DateOnly(2025, 12, 31),
				ForecastTarget = "fresh start",
			},
		};

		var clone = await Vault.WithScopeAsync(services => Task.FromResult(
			(PolarisCycle)services.GetRequiredService<VaultEntityGateway>().CloneScalars(cycle)));

		Assert.NotNull(clone.Forecast);
		Assert.NotSame(cycle.Forecast, clone.Forecast);
		Assert.Equal(cycle.Forecast.ForecastReference, clone.Forecast.ForecastReference);
		Assert.Equal(cycle.Forecast.ForecastTarget, clone.Forecast.ForecastTarget);
	}

	[Fact]
	public async Task Gateway_lookups_span_family_anchors_and_reject_non_puck_types()
	{
		var lunar = await Vault.WithScopeAsync(services => services
			.GetRequiredService<Pleiades.Plaintorch.Api.Abstractions.IDirectiveApi>()
			.CreateLunarAsync("Sleep Law", cancellationToken: TestContext.Current.CancellationToken));

		// The abstract anchor spans its whole discriminated family.
		var viaAnchor = await Vault.WithScopeAsync(services => services
			.GetRequiredService<VaultEntityGateway>()
			.FindByIdAsync(typeof(Directive), lunar.Id, track: false, TestContext.Current.CancellationToken));
		Assert.IsType<LunarDirective>(viaAnchor);

		await Assert.ThrowsAsync<InvalidOperationException>(() => Vault.WithScopeAsync(services => services
			.GetRequiredService<VaultEntityGateway>()
			.FindByIdAsync(typeof(Timeframe), "1", track: false, TestContext.Current.CancellationToken)));
	}
}
