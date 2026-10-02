import type { PlaintorchCoreClient } from "../coreClient"
import type { Objective } from "../objectives/models"
import type { Directive, DirectiveTimeframeRecord } from "../directives/models"
import type { Decree, Fate } from "../declaratives/models"
import type { ExecutiveOrder, OnrushSprint } from "../onrush/models"
import type { PolarisAgenda, PolarisCycle } from "../polaris/models"
import type { LorePage } from "../lore/models"
import type { Checkpoint, Dependency } from "../dependencies/models"
import type { EntityExistence, SystemBriefing } from "../system/models"
import type { PreferenceView } from "../preferences/contracts"
import type { EntityRepository } from "../repository/entityRepository"
import type { DerivedRepository } from "../repository/derivedRepository"
import { RepositorySet } from "../repository/repositorySet"
import { PlaintorchChangeFeed } from "./changeFeed"
import { plaintorchDependents } from "./dependents"

// Imported for their load-time `@model` registration side effect: absorption can reconstruct an entity into
// its class only once that class is registered, and these modules are otherwise reached only through erased
// `import type`. The set refuses to route a type that has not registered, so these are what keep that guard
// honest regardless of how the client is first imported.
import "../objectives/models"
import "../directives/models"
import "../declaratives/models"
import "../onrush/models"
import "../polaris/models"
import "../lore/models"
import "../dependencies/models"

/** Key under which the single briefing record is cached. */
export const briefingRecordKey = ""

export interface PlaintorchRepositoriesOptions {
	/** How long a resolved note or PUCK lookup is served before it is revalidated. */
	resolutionFreshnessMs?: number
}

/**
 * The repository surface of a core client.
 *
 * One repository per tracked entity type, plus derived records for the views the UI subscribes to. The
 * method names mirror the domain SDKs, which the repositories delegate to — the SDK stays the way to talk
 * to the core directly, and this is the way to display something and keep it current.
 *
 * Everything that spans the repositories — routing by type name, invalidation, the eviction sweep — is the
 * {@link RepositorySet} this extends. What is declared here is only which repositories PLAINTORCH has. Each is
 * created through the set, which is what routes it and counts it as an eviction root; the periodic sweep is
 * the eviction trigger chosen for this version, the others considered (idle-detected, on listing refresh,
 * memory-threshold) being recorded in PEP106.
 */
export class PlaintorchRepositories extends RepositorySet {
	/**
	 * Carries writes made outside this client — the watcher, the CLI, the scheduler — back to the store.
	 * Not started automatically; the host decides when to listen.
	 */
	public readonly changeFeed: PlaintorchChangeFeed

	public readonly objectives: EntityRepository<Objective>
	public readonly fates: EntityRepository<Fate>
	public readonly decrees: EntityRepository<Decree>
	/** Stellar directives, which the core serializes under the base `Directive` type. */
	public readonly directives: EntityRepository<Directive>
	public readonly lunarDirectives: EntityRepository<Directive>
	public readonly onrush: EntityRepository<OnrushSprint>
	/** Executive orders (PEP102.5). PUCK-addressable; owned by an onrush sprint. */
	public readonly executiveOrders: EntityRepository<ExecutiveOrder>
	public readonly polaris: EntityRepository<PolarisCycle>
	public readonly lore: EntityRepository<LorePage>
	/** Checkpoints (PEP101). PUCK-addressable like the rest, but database-only — they carry no note. */
	public readonly checkpoints: EntityRepository<Checkpoint>

	/**
	 * Full listings, cached under {@link briefingRecordKey}.
	 *
	 * The entities inside are canonical, so a surface showing one of them individually and a surface
	 * showing the listing agree without either refetching for the other. What the record adds is
	 * membership — which entities exist, and how they relate.
	 */
	public readonly directiveList: DerivedRepository<Directive[]>
	public readonly objectiveList: DerivedRepository<Objective[]>
	public readonly fateList: DerivedRepository<Fate[]>
	public readonly decreeList: DerivedRepository<Decree[]>
	/** Every lore page, in hierarchy order — the record the lore grid observes and rebuilds its tree from. */
	public readonly loreList: DerivedRepository<LorePage[]>

	/**
	 * Every dependency edge, and every checkpoint (PEP101).
	 *
	 * Edges are listed rather than tracked by identity: a {@link Dependency} has a numeric key and no PUCK
	 * token, and its endpoints are loose references the core deliberately leaves without foreign keys. There
	 * is nothing to absorb into the store, so the listing itself is the record a graph surface observes.
	 */
	public readonly dependencyList: DerivedRepository<Dependency[]>
	public readonly checkpointList: DerivedRepository<Checkpoint[]>

	/**
	 * The onrush sprint currently being run, and the one being planned.
	 *
	 * Kept as records rather than reached through {@link onrush} by id, because which sprint is current is
	 * itself the question a surface asks — it has no identifier to look up until the core answers.
	 */
	public readonly onrushCurrent: DerivedRepository<OnrushSprint>
	public readonly onrushPlanning: DerivedRepository<OnrushSprint>

	/**
	 * The day-level agenda: unbound attentives requiring attention and upcoming eventives (PEP100).
	 *
	 * Kept as a derived record rather than reached by identity — it is computed relative to today, not owned
	 * by any entity — so a surface can observe it and let it revalidate on the feed like the briefing.
	 */
	public readonly agenda: DerivedRepository<PolarisAgenda>

	/**
	 * The timeframes active right now (PEP100 patch 2), cached under {@link briefingRecordKey}.
	 *
	 * A derived record like {@link agenda}: which timeframes are active is a question about the clock and the
	 * active Polaris cycle, not a property of any one timeframe. Every surface that reads it — the active-timeframe
	 * chips, the flare on an affined executive — shares this one listing, so they never disagree and a minute's
	 * re-read is one request however many rows are watching. A write revalidates it while observed, like every
	 * record; the passing of time does not, so an observer re-reads it on a clock of its own.
	 */
	public readonly activeTimeframes: DerivedRepository<DirectiveTimeframeRecord[]>

	/**
	 * Every vault-bound user preference with its resolved value (PEP116), cached under {@link briefingRecordKey}.
	 *
	 * Surfaces read settings that shape how they draw from here — above all the preferred calendar an orbit is read on
	 * when its entity names none (`preferredCalendar`) — so one listing serves every chip. The core announces a
	 * preference write on the change feed, which revalidates this while observed like every record.
	 */
	public readonly preferences: DerivedRepository<PreferenceView[]>

	/** The system briefing, cached under {@link briefingRecordKey}. */
	public readonly briefing: DerivedRepository<SystemBriefing>
	/** Note-to-entity resolutions, keyed by vault-relative path. */
	public readonly noteResolution: DerivedRepository<EntityExistence>
	/** PUCK-to-entity resolutions, keyed by PUCK. */
	public readonly entityResolution: DerivedRepository<EntityExistence>

	public constructor(client: PlaintorchCoreClient, options: PlaintorchRepositoriesOptions = {}) {
		super(client.store, { dependents: plaintorchDependents })

		this.objectives = this.entity("Objective", (id) => client.objectives.get(id))
		this.fates = this.entity("Fate", (id) => client.declaratives.getFate(id))
		this.decrees = this.entity("Decree", (id) => client.declaratives.getDecree(id))
		this.directives = this.entity("StellarDirective", (id) => client.directives.get(id))
		this.lunarDirectives = this.entity("LunarDirective", (id) => client.directives.get(id))
		this.onrush = this.entity("OnrushSprint", (id) => client.onrush.get(id))
		this.executiveOrders = this.entity("ExecutiveOrder", (id) => client.onrush.getExecutiveOrder(id))
		this.polaris = this.entity("PolarisCycle", (id) => client.polaris.get(id))
		this.lore = this.entity("LorePage", (id) => client.lore.get(id))
		this.checkpoints = this.entity("Checkpoint", (id) => client.dependencies.getCheckpoint(id))

		this.directiveList = this.derived(async () => await client.directives.list())
		this.objectiveList = this.derived(async () => await client.objectives.list())
		this.fateList = this.derived(async () => await client.declaratives.listFates())
		this.decreeList = this.derived(async () => await client.declaratives.listDecrees())
		this.loreList = this.derived(async () => await client.lore.list())

		this.dependencyList = this.derived(async () => await client.dependencies.list())
		this.checkpointList = this.derived(async () => await client.dependencies.listCheckpoints())
		this.onrushCurrent = this.derived(async () => await client.onrush.getCurrent())
		this.onrushPlanning = this.derived(async () => await client.onrush.getPlanning())
		this.agenda = this.derived(async () => await client.polaris.getAgenda())
		this.activeTimeframes = this.derived(async () => await client.directives.listActiveTimeframes())
		this.preferences = this.derived(async () => await client.preferences.list())

		const resolution = { freshnessMs: options.resolutionFreshnessMs }
		this.briefing = this.derived(async () => await client.system.getBriefing())
		this.noteResolution = this.derived(async (path) => await client.system.resolveNote(path), resolution)
		this.entityResolution = this.derived(async (puck) => await client.system.resolveEntity(puck), resolution)

		this.changeFeed = new PlaintorchChangeFeed(client, this)
	}
}
