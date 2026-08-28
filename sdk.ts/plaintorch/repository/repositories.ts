import type { PlaintorchCoreClient } from "../coreClient"
import type { Objective } from "../objectives/models"
import type { Directive } from "../directives/models"
import type { Decree, Fate } from "../declaratives/models"
import type { ExecutiveOrder, OnrushSprint } from "../onrush/models"
import type { PolarisAgenda, PolarisCycle } from "../polaris/models"
import type { LorePage } from "../lore/models"
import type { Checkpoint, Dependency } from "../dependencies/models"
import type { EntityExistence, SystemBriefing } from "../system/models"
import { EntityRepository, type EntityFetcher } from "./entityRepository"
import { DerivedRepository } from "./derivedRepository"
import { InvalidationScheduler, type InvalidationTarget } from "./invalidation"
import { PlaintorchChangeFeed } from "./changeFeed"
import { entityKey, type EntityKey, type EntityTypeName } from "./identity"

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
 */
export class PlaintorchRepositories implements InvalidationTarget {
	/** Queues what a write makes stale and revalidates whatever of it is on screen. */
	public readonly invalidation: InvalidationScheduler

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

	/** The system briefing, cached under {@link briefingRecordKey}. */
	public readonly briefing: DerivedRepository<SystemBriefing>
	/** Note-to-entity resolutions, keyed by vault-relative path. */
	public readonly noteResolution: DerivedRepository<EntityExistence>
	/** PUCK-to-entity resolutions, keyed by PUCK. */
	public readonly entityResolution: DerivedRepository<EntityExistence>

	private readonly byTypeName = new Map<EntityTypeName, EntityRepository<never>>()

	public constructor(private readonly client: PlaintorchCoreClient, options: PlaintorchRepositoriesOptions = {}) {
		const store = client.store
		// Resolved lazily: the scheduler reaches back into the repositories it is handed to.
		this.invalidation = new InvalidationScheduler(store, () => this)
		const entity = { invalidation: this.invalidation }

		// Registering an entity repository also routes it by type name, so one cannot be added without
		// `forTypeName` — and the change feed and invalidation that lean on it — finding it. That omission is
		// exactly what once silently stopped every directive from being tracked.
		const register = <T extends object>(typeName: EntityTypeName, fetcher: EntityFetcher<T>): EntityRepository<T> => {
			const repository = new EntityRepository<T>(store, typeName, fetcher, entity)
			this.byTypeName.set(typeName, repository as EntityRepository<never>)
			return repository
		}

		this.objectives = register("Objective", (id) => client.objectives.get(id))
		this.fates = register("Fate", (id) => client.declaratives.getFate(id))
		this.decrees = register("Decree", (id) => client.declaratives.getDecree(id))
		this.directives = register("StellarDirective", (id) => client.directives.get(id))
		this.lunarDirectives = register("LunarDirective", (id) => client.directives.get(id))
		this.onrush = register("OnrushSprint", (id) => client.onrush.get(id))
		this.executiveOrders = register("ExecutiveOrder", (id) => client.onrush.getExecutiveOrder(id))
		this.polaris = register("PolarisCycle", (id) => client.polaris.get(id))
		this.lore = register("LorePage", (id) => client.lore.get(id))
		this.checkpoints = register("Checkpoint", (id) => client.dependencies.getCheckpoint(id))

		this.directiveList = new DerivedRepository(async () => await client.directives.list())
		this.objectiveList = new DerivedRepository(async () => await client.objectives.list())
		this.fateList = new DerivedRepository(async () => await client.declaratives.listFates())
		this.decreeList = new DerivedRepository(async () => await client.declaratives.listDecrees())
		this.loreList = new DerivedRepository(async () => await client.lore.list())

		this.dependencyList = new DerivedRepository(async () => await client.dependencies.list())
		this.checkpointList = new DerivedRepository(async () => await client.dependencies.listCheckpoints())
		this.onrushCurrent = new DerivedRepository(async () => await client.onrush.getCurrent())
		this.onrushPlanning = new DerivedRepository(async () => await client.onrush.getPlanning())
		this.agenda = new DerivedRepository(async () => await client.polaris.getAgenda())

		const resolution = { freshnessMs: options.resolutionFreshnessMs }
		this.briefing = new DerivedRepository(async () => await client.system.getBriefing())
		this.noteResolution = new DerivedRepository(async (path) => await client.system.resolveNote(path), resolution)
		this.entityResolution = new DerivedRepository(async (puck) => await client.system.resolveEntity(puck), resolution)

		this.changeFeed = new PlaintorchChangeFeed(client, this)
	}

	/**
	 * Resolves the repository responsible for a runtime type name.
	 *
	 * Lets a generic surface — a banner that only knows which kind of note it is rendering — resolve and
	 * observe an entity without a switch over every type.
	 */
	public forTypeName<T extends object>(typeName: string | undefined): EntityRepository<T> | undefined {
		return typeName === undefined
			? undefined
			: this.byTypeName.get(typeName as EntityTypeName) as EntityRepository<T> | undefined
	}

	/**
	 * Revalidates every derived record that something is observing.
	 *
	 * Used as the coarse fallback when a promptness signal is unavailable — on surface activation, or when
	 * a change feed reconnects after missing events.
	 */
	public async revalidateObservedRecords(): Promise<void> {
		await Promise.all(this.records.map(async (record) => await record.revalidateObserved()))
	}

	private get records(): DerivedRepository<unknown>[] {
		return [
			this.briefing,
			this.noteResolution,
			this.entityResolution,
			this.directiveList as DerivedRepository<unknown>,
			this.objectiveList as DerivedRepository<unknown>,
			this.fateList as DerivedRepository<unknown>,
			this.decreeList as DerivedRepository<unknown>,
			this.loreList as DerivedRepository<unknown>,
			this.dependencyList as DerivedRepository<unknown>,
			this.checkpointList as DerivedRepository<unknown>,
			this.onrushCurrent as DerivedRepository<unknown>,
			this.onrushPlanning as DerivedRepository<unknown>,
			this.agenda as DerivedRepository<unknown>
		]
	}

	/** @inheritdoc */
	public async revalidateRecordsIfObserved(): Promise<void> {
		await this.revalidateObservedRecords()
	}

	/**
	 * @inheritdoc
	 *
	 * Routed by type name so the scheduler can name a dependency of any kind without knowing which
	 * repository serves it.
	 */
	public async revalidateEntityIfObserved(key: EntityKey): Promise<void> {
		const separator = key.indexOf(":")
		const typeName = key.slice(0, separator)
		const id = key.slice(separator + 1)
		await this.forTypeName(typeName)?.revalidateIfObserved(id)
	}

	/**
	 * Revalidates everything currently on screen — entities and records alike.
	 *
	 * The coarse recovery path, for when promptness cannot be relied on: a change feed reconnecting after
	 * missing events, or a host waking a surface back up.
	 */
	public async revalidateObserved(): Promise<void> {
		await Promise.all([
			...[...this.byTypeName.values()].map(async (repository) => await repository.revalidateObserved()),
			this.revalidateObservedRecords()
		])
	}

	/**
	 * Gives up the local claim on an identity, so the next read of it applies whatever its age.
	 *
	 * Used for changes the core declares authoritative. A write still in flight keeps its own protection —
	 * this only concedes the ordering, it does not interrupt anything.
	 */
	public acceptAuthority(typeName: string, id: string): void {
		this.client.store.acceptAuthority(entityKey(typeName, id))
	}

	/** Marks everything as needing revalidation, without fetching anything. */
	public invalidateAll(): void {
		this.client.store.markAllStale()
		for (const record of this.records) {
			record.invalidate()
		}
	}
}
