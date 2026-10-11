using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Puck;
using Pleiades.Saga;
using Pleiades.Diagnostics;
using Pleiades.Plaintorch.Api;
using Pleiades.Plaintorch.Api.Changes;
using Pleiades.Plaintorch.Diagnostics;
using Pleiades.Plaintorch.Enforcement;
using Pleiades.Plaintorch.Hosting;
using Pleiades.Plaintorch.Materialization;
using Pleiades.Plaintorch.Media;
using Pleiades.Plaintorch.Preferences;
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
		services.AddSingleton<PlaintorchHostState>();
		services.AddSingleton<VaultLayout>(serviceProvider =>
			serviceProvider.GetService<VaultOptions>() is { } vaultOptions
				? new VaultLayout(vaultOptions)
				: new VaultLayout());
		services.AddScoped<DependencyGateService>();
		// The one reading of the dependency graph — stored edges plus the intrinsic edges each rule derives (PEP102
		// patch 9) — shared by the lock readings, the API and the purge. The judge derives the same intrinsic edges, and
		// the reconciler reads only the stored ones.
		services.AddScoped<DependencyGraphService>();
		services.AddScoped<IIntrinsicDependencyRule, DirectiveChildrenIntrinsicRule>();
		// PEP103: the state enforcement engine, its four phases, and the one save interceptor that runs it on every save.
		services.AddScoped<StructureReactions>();
		services.AddScoped<MembershipReactions>();
		services.AddScoped<StateJudge>();
		services.AddScoped<StateCompensator>();
		services.AddScoped<DependencyReconciler>();
		services.AddScoped<OutcomeReactions>();
		services.AddScoped<BacklogScheduleRecorder>();
		services.AddScoped<StateEngine>();
		services.AddScoped<StateNoteSyncService>();
		services.AddScoped<StateInterceptor>();
		services.AddSingleton<PlaintorchChangeBroker>();
		services.AddScoped<PlaintorchChangeFeedInterceptor>();
		// The active lore spine, cached once per vault session and stamped onto responses by a filter rather
		// than hand-computed at each lore endpoint; the interceptor drops the cache when a lore page is written.
		services.AddSingleton<LoreActiveCache>();
		services.AddScoped<LoreActiveCacheInterceptor>();
		// The active Polaris cycle's timeframe candidates (PEP100 patch 2), warmed at cycle begin and vault activation;
		// the interceptor drops them on any write that can change the set, across every write pathway.
		services.AddSingleton<TimeframeCandidateCache>();
		services.AddScoped<TimeframeCandidateCacheInterceptor>();
		services.AddDbContext<PlainfraContext>((serviceProvider, options) =>
		{
			// The store, with WAL and a busy timeout on every connection so the concurrent reads a client fires around a
			// write do not meet a whole-file lock with no timeout — shared with the bare contexts PUCK issuance uses.
			PlainfraStoreContextFactory.ConfigureStore(options, serviceProvider.GetRequiredService<VaultLayout>());
			// The one place the backlog's state is enforced (PEP103), on synchronous and asynchronous saves alike: structure,
			// judgement through the enforcement level, outcomes and records, in one pass before anything is written.
			options.AddInterceptors(serviceProvider.GetRequiredService<StateInterceptor>());
			// Registered after the state interceptor, so the changes it announces are the ones the engine left behind rather
			// than what the caller originally asked for.
			options.AddInterceptors(serviceProvider.GetRequiredService<PlaintorchChangeFeedInterceptor>());
			options.AddInterceptors(serviceProvider.GetRequiredService<LoreActiveCacheInterceptor>());
			options.AddInterceptors(serviceProvider.GetRequiredService<TimeframeCandidateCacheInterceptor>());
		});

		// Bare contexts over the same store, with none of the save hooks above: PUCK issuance registers its ids on one of
		// its own, so minting an id never saves what the caller's context has pending.
		services.AddSingleton<PlainfraStoreContextFactory>();
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
		services.AddSingleton<ILifecycleStateSource, EventiveLifecycleStateSource>();
		services.AddSingleton<ILifecycleStateSource, CheckpointLifecycleStateSource>();
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
		// PEP116 user preferences: a sparse, vault-bound key/value store with code-owned defaults, surfaced
		// through .NET Options. The store is the singleton hot-read cache (loaded on activation, kept current by
		// write-through); the service owns the database read/write; each Options group binds its keys off the
		// store, so an unset preference falls through to its POCO-default and adding one needs no migration.
		services.AddSingleton<UserPreferenceStore>();
		services.AddScoped<UserPreferenceService>();
		services.AddOptions<WatcherPreferences>().Configure<UserPreferenceStore>((preferences, store) =>
		{
			preferences.NoteQueueTimeout = store.Get(PreferenceKeys.NoteQueueTimeout, preferences.NoteQueueTimeout);
		});
		services.AddOptions<AgendaPreferences>().Configure<UserPreferenceStore>((preferences, store) =>
		{
			preferences.AutoMaterialiseOptOut = store.Get(PreferenceKeys.AutoMaterialiseOptOut, preferences.AutoMaterialiseOptOut);
			preferences.DefaultCalendar = store.Get(PreferenceKeys.DefaultCalendar, preferences.DefaultCalendar);
		});
		services.AddOptions<ReflectionPreferences>().Configure<UserPreferenceStore>((preferences, store) =>
		{
			preferences.BaseDailyReflectives = store.Get(PreferenceKeys.BaseDailyReflectives, preferences.BaseDailyReflectives);
			preferences.DailyReward = store.Get(PreferenceKeys.ReflectionDailyReward, preferences.DailyReward);
		});
		services.AddOptions<CalDavPreferences>().Configure<UserPreferenceStore>((preferences, store) =>
		{
			preferences.FloatingRender = store.Get(PreferenceKeys.CalDavFloatingRender, preferences.FloatingRender);
		});
		services.AddOptions<BacklogPreferences>().Configure<UserPreferenceStore>((preferences, store) =>
		{
			preferences.StateEnforcement = store.Get(PreferenceKeys.BacklogStateEnforcement, preferences.StateEnforcement);
		});
		// PEP103 state enforcement: the request-wide API flags (set by the API, defaulted elsewhere) and the one place a
		// requested change is judged against the backlog state enforcement level.
		services.AddScoped<PlaintorchRequestFlags>();
		services.AddScoped<StateDecisionService>();
		services.AddSingleton<WatcherStatusReporter>();
		services.AddSingleton<WatcherRetryScheduler>();
		services.AddSingleton<VaultStorageTopologyValidator>();
		services.AddSingleton<PlaintorchVaultLockService>();
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
		services.AddScoped<VaultWatcherReconciler>();
		services.AddScoped<VaultAutoGeneratedPuckConsistencyService>();
		services.AddSingleton<VaultVersionService>();
		services.AddScoped<VaultConventionSetFactory>();
		services.AddScoped<VaultLoader>();
		services.AddScoped<VaultMigrationRunner>();
		services.AddScoped<IVaultMigration, ObjectiveQuietCanonicalizationMigration>();
		services.AddScoped<IVaultMigration, OnrushPlanningPlaceholderRenameMigration>();
		services.AddScoped<IVaultMigration, FateOrbitOnlyMigration>();
		services.AddScoped<IVaultMigration, DirectiveStartEndRetirementMigration>();
		services.AddScoped<IVaultMigration, IntrinsicDependencyPurgeMigration>();
		services.AddScoped<IVaultMigration, ObjectiveOnboardStatusMigration>();
		services.AddScoped<IVaultMigration, FateStoppedStatusMigration>();
		services.AddScoped<PlaintorchEngine>();
		services.AddScoped<TimeframeAffinityResolver>();
		services.AddScoped<TimeframeCandidateService>();
		services.AddSingleton<MaterializationPolicyOptions>();
		services.AddSingleton<TerminativeService>();
		services.AddScoped<OccurrenceHardeningService>();
		services.AddScoped<AgendaProjectionService>();
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