import type { PlaintorchCoreClient } from "../coreClient"
import type { Objective } from "../objectives/models"
import type { Directive, DirectiveTimeframeRecord } from "../directives/models"
import type { Decree, Fate } from "../declaratives/models"
import type { ExecutiveOrder, OnrushSprint } from "../onrush/models"
import type { LorePolarisCycles, PolarisAgenda, PolarisCycle } from "../polaris/models"
import type { LorePage } from "../lore/models"
import type { Checkpoint, Dependency } from "../dependencies/models"
import type { EntityExistence, SystemBriefing } from "../system/models"
import type { PreferenceView } from "../preferences/contracts"
import type { LifecycleKind } from "../lifecycle/models"
import { type EntityRepository, type DerivedRepository, RepositorySet } from "@corruptedlotus/local-repo"
import { answered, answeringSdks } from "./answered"
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
	 * Polaris cycles day by day — the forecast surface's record — keyed by the ISO date of the first day wanted
	 * (`2026-09-30`): every cycle from that day on, forecast, active and ended alike, in day order.
	 *
	 * Unbounded ahead on purpose. A surface planning forward must know the LAST planned day wherever it lies — which
	 * days it offers for planning depends on it — not only the days it happens to show, and the days ahead hold
	 * forecasts only, which stay few. The cycles inside are canonical, so a column showing one and the briefing card
	 * showing the same cycle agree.
	 */
	public readonly polarisDays: DerivedRepository<PolarisCycle[]>

	/**
	 * The Polaris cycles of a lore page's active period, keyed by the page's PUCK (`Era1/Cha2`).
	 *
	 * A derived record because the period is the core's call: it turns on the page's beginning, its siblings' and its
	 * ancestors', and on today. A write revalidates it while observed, like every record, so a beginning edited beside it
	 * re-reads the period. The cycles inside are canonical.
	 */
	public readonly lorePolaris: DerivedRepository<LorePolarisCycles>

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
	/** How each kind's lifecycle reads (PEP103), the core's declarations as served. They never change while it runs. */
	public readonly lifecycles: DerivedRepository<LifecycleKind[]>

	/** The system briefing, cached under {@link briefingRecordKey}. */
	public readonly briefing: DerivedRepository<SystemBriefing>
	/** Note-to-entity resolutions, keyed by vault-relative path. */
	public readonly noteResolution: DerivedRepository<EntityExistence>
	/** PUCK-to-entity resolutions, keyed by PUCK. */
	public readonly entityResolution: DerivedRepository<EntityExistence>

	public constructor(client: PlaintorchCoreClient, options: PlaintorchRepositoriesOptions = {}) {
		super(client.store, { dependents: plaintorchDependents })
		// Every fetcher reads through these rather than through the client's own SDKs, so a read the core did not
		// answer comes back as unresolved, and what is on screen is kept, instead of coming back as emptiness.
		const read = answeringSdks(client)

		this.objectives = this.entity("Objective", (id) => answered(() => read.objectives.get(id)))
		this.fates = this.entity("Fate", (id) => answered(() => read.declaratives.getFate(id)))
		this.decrees = this.entity("Decree", (id) => answered(() => read.declaratives.getDecree(id)))
		this.directives = this.entity("StellarDirective", (id) => answered(() => read.directives.get(id)))
		this.lunarDirectives = this.entity("LunarDirective", (id) => answered(() => read.directives.get(id)))
		this.onrush = this.entity("OnrushSprint", (id) => answered(() => read.onrush.get(id)))
		this.executiveOrders = this.entity("ExecutiveOrder", (id) => answered(() => read.onrush.getExecutiveOrder(id)))
		this.polaris = this.entity("PolarisCycle", (id) => answered(() => read.polaris.get(id)))
		this.lore = this.entity("LorePage", (id) => answered(() => read.lore.get(id)))
		this.checkpoints = this.entity("Checkpoint", (id) => answered(() => read.dependencies.getCheckpoint(id)))

		this.directiveList = this.derived(() => answered(() => read.directives.list()))
		this.objectiveList = this.derived(() => answered(() => read.objectives.list()))
		this.fateList = this.derived(() => answered(() => read.declaratives.listFates()))
		this.decreeList = this.derived(() => answered(() => read.declaratives.listDecrees()))
		this.loreList = this.derived(() => answered(() => read.lore.list()))

		this.dependencyList = this.derived(() => answered(() => read.dependencies.list()))
		this.checkpointList = this.derived(() => answered(() => read.dependencies.listCheckpoints()))
		this.onrushCurrent = this.derived(() => answered(() => read.onrush.getCurrent()))
		this.onrushPlanning = this.derived(() => answered(() => read.onrush.getPlanning()))
		this.agenda = this.derived(() => answered(() => read.polaris.getAgenda()))
		this.polarisDays = this.derived((from) => answered(() => read.polaris.listByDay(from)))
		this.lorePolaris = this.derived((lorePuck) => answered(() => read.polaris.listForLore(lorePuck)))
		this.activeTimeframes = this.derived(() => answered(() => read.directives.listActiveTimeframes()))
		this.preferences = this.derived(() => answered(() => read.preferences.list()))
		this.lifecycles = this.derived(() => answered(() => read.system.getLifecycles()))

		const resolution = { freshnessMs: options.resolutionFreshnessMs }
		this.briefing = this.derived(() => answered(() => read.system.getBriefing()))
		this.noteResolution = this.derived((path) => answered(() => read.system.resolveNote(path)), resolution)
		this.entityResolution = this.derived((puck) => answered(() => read.system.resolveEntity(puck)), resolution)

		this.changeFeed = new PlaintorchChangeFeed(client, this)
	}
}
