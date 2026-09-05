using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Puck;
using Pleiades.Diagnostics;
using Pleiades.Plaintorch.Api.Changes;
using Pleiades.Plaintorch.Diagnostics;
using Pleiades.Plaintorch.Materialization;
using Pleiades.Plaintorch.Media;
using Pleiades.Plaintorch.State;
using Pleiades.Vault;
using Pleiades.Vault.Database;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Media;
using Pleiades.Vault.Migration;
using Pleiades.Vault.Migration.Migrations;
using Pleiades.Vault.Policy;
using Pleiades.Vault.Watcher;

namespace Pleiades.Plaintorch;

/// <summary>
/// Registers the current PLAINTORCH application services through the A11d module pipeline.
/// </summary>
public sealed class PlaintorchModule : Module
{
	/// <inheritdoc />
	public override void ConfigureServices(IServiceCollection services)
	{
		services.TryAddSingleton(_ => PlaintorchUserLayout.CreateDefault());
		services.AddSingleton<PlaintorchUserConfigurationStore>();
		services.AddSingleton<PlaintorchVaultActivationService>();
		services.AddSingleton<ActiveVaultSession>();
		services.AddSingleton<VaultLayout>(serviceProvider =>
			serviceProvider.GetService<VaultOptions>() is { } vaultOptions
				? new VaultLayout(vaultOptions)
				: new VaultLayout());
		services.AddScoped<DependencyReconciler>();
		services.AddScoped<DependencyGateService>();
		services.AddScoped<PlaintorchStatePolicyProcessor>();
		services.AddScoped<PlaintorchStatePolicyFileSyncService>();
		services.AddScoped<PlaintorchStatePolicyInterceptor>();
		services.AddSingleton<PlaintorchChangeBroker>();
		services.AddScoped<PlaintorchChangeFeedInterceptor>();
		services.AddDbContext<PlainfraContext>((serviceProvider, options) =>
		{
			var layout = serviceProvider.GetRequiredService<VaultLayout>();
			Directory.CreateDirectory(layout.MetadataRoot);
			options.UseSqlite($"Data Source={layout.DatabasePath}");
			// Sets WAL and a busy timeout on every connection so the concurrent reads a client fires around a
			// write do not meet a whole-file lock with no timeout. Stateless, so a fresh instance per context.
			options.AddInterceptors(new SqlitePragmaConnectionInterceptor());
			options.AddInterceptors(serviceProvider.GetRequiredService<PlaintorchStatePolicyInterceptor>());
			// Registered after the state policy, so the changes it announces are the ones policy left
			// behind rather than what the caller originally asked for.
			options.AddInterceptors(serviceProvider.GetRequiredService<PlaintorchChangeFeedInterceptor>());
		});

		services.AddScoped<PlainfraContextInitializer>();
		services.AddScoped<VaultBootstrapper>();
		services.AddScoped<PlaintorchRepository>();
		services.AddScoped<VaultTemporalDataService>();
		services.AddScoped<VaultAuditLogService>();
		services.AddScoped<VaultMediaService>();
		services.AddScoped<MediaAssetFolderResolver>();
		services.AddScoped<MediaResponseEnricher>();
		services.AddScoped<VaultImplicitBoundaryService>();
		services.AddSingleton<PuckNotationParser>();
		services.AddSingleton<PuckRuntimeCompilationCatalog>();
		services.AddSingleton<VaultEntityModelCatalog>();
		services.AddSingleton<ILifecyclePhaseSource, EventiveLifecyclePhaseSource>();
		services.AddSingleton<EntityLifecycleResolver>();
		services.AddScoped<VaultEntityGateway>();
		services.AddScoped<Pleiades.Plaintorch.Api.Services.VaultEntityLifecycleService>();
		services.AddSingleton<PuckTokenizer>();
		services.AddSingleton<PuckIdentityGate>();
		services.AddSingleton<PuckPathDiscriminabilityService>();
		services.AddSingleton<PuckSemanticProjector>();
		services.AddScoped<PuckEntityResolutionService>();
		services.AddScoped<PuckCreationService>();
		services.AddScoped<PuckIdService>();
		services.AddSingleton<PolarisCycleLifecycle>();
		services.AddSingleton<MarkdownFrontMatterSerializer>();
		services.AddSingleton<VaultStoragePathComposer>();
		services.AddSingleton<MarkdownFileLocator>();
		services.AddSingleton<VaultPathSyncModelCatalog>();
		services.AddSingleton<VaultFamilyInstantiationResolver>();
		services.AddSingleton<VaultWatcherPathPolicy>();
		services.AddSingleton<VaultWatcherWriteBarrier>();
		// PEP108 operation-status core: live registry, buffered durable sink, and reporter (watcher is the first consumer).
		services.AddSingleton<OperationStatusRegistry>();
		services.AddSingleton<OperationStatusEventBuffer>();
		services.AddSingleton<IOperationStatusSink>(provider => provider.GetRequiredService<OperationStatusEventBuffer>());
		services.AddSingleton<OperationStatusReporter>();
		services.AddScoped<OperationStatusDismissalService>();
		services.AddSingleton<WatcherStatusReporter>();
		services.AddSingleton<VaultStorageTopologyValidator>();
		services.AddSingleton<PlaintorchVaultLockService>();
		services.AddSingleton<PlaintorchCoreSplashService>();
		services.AddSingleton<PlaintorchServiceBootstrapper>();
		services.AddScoped<IVaultStorageModePolicyService, FreeformVaultStorageModePolicyService>();
		services.AddScoped<IVaultStorageModePolicyService, ImplicitVaultStorageModePolicyService>();
		services.AddScoped<IVaultStorageModePolicyService, EnforcedVaultStorageModePolicyService>();
		services.AddScoped<IVaultStorageModePolicyService, OptionalVaultStorageModePolicyService>();
		services.AddScoped<IVaultStorageModePolicyService, SyncedVaultStorageModePolicyService>();
		services.AddScoped<IVaultStorageModePolicyService, FileFirstVaultStorageModePolicyService>();
		services.AddScoped<VaultStorageModePolicyRouter>();
		services.AddScoped<VaultStoragePolicyEngine>();
		services.AddScoped<VaultSyncDecisionService>();
		services.AddScoped<VaultMarkdownDiscoveryService>();
		services.AddScoped<VaultWatcherSyncService>();
		services.AddScoped<VaultAutoGeneratedPuckConsistencyService>();
		services.AddSingleton<VaultVersionService>();
		services.AddScoped<VaultConventionSetFactory>();
		services.AddScoped<VaultLoader>();
		services.AddScoped<VaultMigrationRunner>();
		services.AddScoped<IVaultMigration, ObjectiveQuietCanonicalizationMigration>();
		services.AddScoped<PlaintorchEngine>();
		services.AddScoped<TimeframeAffinityResolver>();
		services.AddScoped<ProximityMaterializationService>();
		services.AddHostedService<PlaintorchCoreService>();
		services.AddHostedService<VaultWatcherService>();
		services.AddHostedService<RollingMaterializationService>();
		services.AddHostedService<OperationStatusPersistenceWorker>();
	}

	/// <inheritdoc />
	public override void ConfigureApplication(WebApplication application)
	{
	}

	/// <inheritdoc />
	public override void ConfigureEndpoints(IEndpointRouteBuilder endpoints)
	{
	}
}